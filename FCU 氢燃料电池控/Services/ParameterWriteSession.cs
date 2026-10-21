using System;
using System.Linq;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>UI线程驱动的普通参数单事务。等待旧查询、只写一次、ACK后回读，不盲重试Flash。</summary>
    public sealed class ParameterWriteSession
    {
        private enum Phase { Idle, Draining, Writing, Verifying }
        private volatile Phase _phase;
        private readonly Func<byte[], bool> _send;
        private readonly Func<bool> _canWrite;
        private readonly Func<long> _clock;
        private ParameterWriteRequest? _request;
        private volatile byte[]? _ownedFrame;
        private long _started;
        private long _quietUntil;
        public bool IsBusy => _phase != Phase.Idle;
        public bool MayHaveLateAck => _clock() < _quietUntil;
        public bool Verified { get; private set; }
        public int Epoch { get; private set; }
        public ushort? ParameterId => _request?.ParameterId;
        public string Status { get; private set; } = "尚无参数写入操作";

        public ParameterWriteSession(Func<byte[], bool> send, Func<bool> canWrite, Func<long>? clock = null)
        {
            _send = send;
            _canWrite = canWrite;
            _clock = clock ?? (() => Environment.TickCount64);
        }

        public void ReportRejection(string reason)
        {
            if (!IsBusy) { Status = reason; Verified = false; }
        }

        public bool Blocks(byte[] frame)
        {
            if (ReferenceEquals(frame, _ownedFrame)) return false;
            if (!IsBusy)
                return MayHaveLateAck && frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                    frame[3] is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x0B or 0x0E;
            // 安全停机与归还BMS轮询仍可用，其响应码也不与0x82/0x8A混用。
            return !(frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                (frame[3] == 0x09 || (frame.Length == 8 && frame[3] == 0x0D && frame[5] == 1)));
        }

        public bool Write(byte address, byte function, byte[] data, string name)
        {
            if (IsBusy) return false;
            if (MayHaveLateAck)
            {
                Status = "上一条写入结果不明，正在等待迟到响应静默窗口；请稍后重试或先读取核对";
                return false;
            }
            _request = ParameterWriteProtocol.Create(address, function, data, name);
            Epoch++;
            Verified = false;
            _phase = Phase.Draining;
            _started = _clock();
            Status = $"{name}：等待在途查询结束，尚未发送写入";
            Tick();
            return IsBusy || Verified;
        }

        private bool SendOwned(byte[] frame)
        {
            _started = _clock();
            var previous = _ownedFrame;
            _ownedFrame = frame;
            try
            {
                if (_send(frame)) return true;
                Finish($"{_request!.Name}：发送失败，设备结果未确认；请读取核对", true);
            }
            catch (Exception ex)
            {
                Finish($"{_request!.Name}：发送异常：{ex.Message}；请读取核对", true);
            }
            finally { _ownedFrame = previous; }
            return false;
        }

        public bool Receive(byte[] frame)
        {
            var request = _request;
            if (!IsBusy || request == null) return false;
            if (_phase == Phase.Writing && FirmwareMainProtocol.IsResponse(frame, request.Address, request.ResponseFunction, 1))
            {
                byte result = frame[5];
                if (result != 0)
                {
                    Finish($"{request.Name}：写入失败，{ProtocolConstants.GetErrorCodeDescription(result)}" +
                        (result == 9 ? "（固件未保存，已回滚原参数）" : ""));
                }
                else
                {
                    _phase = Phase.Verifying;
                    Status = $"{request.Name}：设备确认成功，正在回读核对";
                    SendOwned(request.ReadFrame);
                }
                return true;
            }
            if (_phase != Phase.Verifying || frame.Length < 10 || frame[2] != request.Address ||
                frame[3] != 0x8A || frame[5] != (byte)(request.ParameterId >> 8) || frame[6] != (byte)request.ParameterId)
                return false;
            if (!FirmwareMainProtocol.IsResponse(frame, request.Address, 0x8A, frame[4])) return false;
            if (!ParameterWriteProtocol.IsValidReadback(frame, request))
            {
                Finish($"{request.Name}：回读格式不匹配，无法确认本次写入；请核对固件与通信日志");
                return true;
            }
            Verified = frame[8..^2].SequenceEqual(request.ExpectedBody);
            Finish(Verified ? $"{request.Name}：设备确认成功，回读一致，已保存"
                : $"{request.Name}：回读与本次写入不一致，输入已保留；请重新读取核对");
            return true;
        }

        public void Tick()
        {
            if (!IsBusy) return;
            if (_clock() - _started >= (_phase == Phase.Draining ? 5000 : 3000))
            {
                bool sent = _phase != Phase.Draining;
                Finish(sent ? $"{_request!.Name}：响应超时，写入结果未确认；请读取核对"
                    : $"{_request!.Name}：在途查询未结束，参数尚未发送，请检查链路后重试", sent);
                return;
            }
            if (_phase == Phase.Draining && _canWrite())
            {
                _phase = Phase.Writing;
                Status = $"{_request!.Name}：已发送，等待设备确认保存";
                SendOwned(_request.WriteFrame);
            }
        }

        public void Reset()
        {
            Epoch++;
            if (IsBusy)
                Finish($"{_request!.Name}：连接、目标或操作模式已改变，写入操作中断；请读取核对", _phase != Phase.Draining);
            else Status = "尚无当前设备的参数写入操作";
            Verified = false;
            _request = null;
        }

        public void CancelIfDraining(string reason)
        {
            if (_phase == Phase.Draining) Finish($"{_request!.Name}：{reason}，参数尚未发送");
        }

        private void Finish(string message, bool unknown = false)
        {
            if (unknown) _quietUntil = _clock() + 3000;
            _phase = Phase.Idle;
            Status = message;
        }
    }
}
