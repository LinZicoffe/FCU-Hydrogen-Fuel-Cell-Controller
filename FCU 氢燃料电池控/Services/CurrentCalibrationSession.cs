using System;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>UI线程单事务；共享0x82需独占，ACK后回读核对，断线不重试写入。</summary>
    public sealed class CurrentCalibrationSession
    {
        private enum Phase { Idle, Reading, Writing, Verifying }
        private volatile Phase _phase;
        private readonly Func<byte[], bool> _send;
        private readonly Func<long> _clock;
        private long _started;
        private long _quietUntil;
        private byte _address;
        private CurrentCalibration? _requested;
        private volatile byte[]? _ownedFrame;
        public bool IsBusy => _phase != Phase.Idle;
        public bool MayHaveLateAck => _clock() < _quietUntil;
        public CurrentCalibration? Actual { get; private set; }
        public bool Verified { get; private set; }
        public int Epoch { get; private set; }
        public string Status { get; private set; } = "尚未读取输入拉载标定，输入框为默认值";

        public CurrentCalibrationSession(Func<byte[], bool> send, Func<long>? clock = null)
        { _send = send; _clock = clock ?? (() => Environment.TickCount64); }

        public void ReportRejection(string message) => Status = message;
        public bool Blocks(byte[] frame)
        {
            if (ReferenceEquals(frame, _ownedFrame)) return false;
            if (!IsBusy)
                return MayHaveLateAck && frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                    frame[3] is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x0B;
            // 安全停机和归还BMS控制权可打断普通事务。
            if (frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                (frame[3] == 0x09 || (frame.Length == 8 && frame[3] == 0x0D && frame[5] == 1))) return false;
            return true;
        }

        public bool Read(byte address) => Begin(address, null);
        public bool Write(byte address, CurrentCalibration values)
        {
            if (!values.IsValid) throw new ArgumentOutOfRangeException(nameof(values));
            return Begin(address, values);
        }

        private bool Begin(byte address, CurrentCalibration? values)
        {
            if (IsBusy) return false;
            if (values != null && MayHaveLateAck)
            {
                Status = "上一条标定写入结果未知，等待迟到应答静默窗口；请稍后重试";
                return false;
            }
            Epoch++; _address = address; _requested = values;
            Actual = null; Verified = false;
            _phase = values == null ? Phase.Reading : Phase.Writing;
            Status = values == null ? "等待输入拉载标定回读" : "等待FCU保存输入拉载标定（仅OFF可写）";
            return SendOwned(values == null ? CurrentCalibrationProtocol.Read(address) : CurrentCalibrationProtocol.Write(address, values));
        }

        private bool SendOwned(byte[] frame)
        {
            _started = _clock(); _ownedFrame = frame;
            try
            {
                if (_send(frame)) return true;
                Finish("标定发送失败，设备结果未知；请重新读取核对", unknown: true); return false;
            }
            finally { _ownedFrame = null; }
        }

        public bool Receive(byte[] frame)
        {
            if (!IsBusy) return false;
            if (_phase == Phase.Writing && FirmwareMainProtocol.IsResponse(frame, _address, 0x82, 1))
            {
                if (frame[5] != 0)
                {
                    string reason = frame[5] == 3 ? "仅电堆OFF可写，停止拉载不等于OFF" : frame[5] == 9 ? "保存失败，固件已回滚" :
                        ProtocolConstants.GetErrorCodeDescription(frame[5]);
                    Finish($"输入拉载标定写入失败：{reason}");
                }
                else
                {
                    _phase = Phase.Verifying; Status = "FCU报告保存成功，正在回读核对k、b";
                    SendOwned(CurrentCalibrationProtocol.Read(_address));
                }
                return true;
            }
            if (_phase is Phase.Reading or Phase.Verifying && frame.Length >= 10 && frame[2] == _address &&
                frame[3] == 0x8A && frame[5] == 3 && frame[6] == 2)
            {
                if (CurrentCalibrationProtocol.TryParse(frame, _address, out var actual))
                {
                    Actual = actual;
                    Verified = _phase == Phase.Verifying && actual == _requested;
                    Finish(_phase == Phase.Reading ? "输入拉载标定读取成功" : Verified ? "输入拉载标定写入并回读一致，已保存" :
                        "输入拉载标定回读不一致，请核对设备k、b");
                    return true;
                }
                if (FirmwareMainProtocol.IsResponse(frame, _address, 0x8A, 3) && frame[7] == 0)
                { Finish("固件不支持输入拉载标定0x0302，输入未改变"); return true; }
                if (FirmwareMainProtocol.IsResponse(frame, _address, 0x8A, 11))
                { Finish("输入拉载标定回读非法（系数或子索引），输入未改变"); return true; }
            }
            return false;
        }

        public void Tick()
        {
            if (IsBusy && _clock() - _started >= 3000) Finish("输入拉载标定响应超时，设备结果未知；请重新读取核对", unknown: true);
        }
        public void Reset()
        {
            Epoch++;
            if (_phase is Phase.Writing or Phase.Verifying) _quietUntil = _clock() + 3000;
            Status = IsBusy ? "连接、目标或操作模式改变，标定操作中断，设备结果未知；请重新读取" : "尚未读取当前设备输入拉载标定";
            _phase = Phase.Idle; Actual = null; Verified = false;
        }
        private void Finish(string message, bool unknown = false)
        {
            if (unknown && _phase is Phase.Writing or Phase.Verifying) _quietUntil = _clock() + 3000;
            _phase = Phase.Idle;
            Status = message;
        }
    }
}
