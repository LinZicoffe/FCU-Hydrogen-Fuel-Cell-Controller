using System;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>UI线程驱动的单事务。0x82不含ID，写成功还需独占回读确认，不盲重试写入。</summary>
    public sealed class StandbyPeriodSession
    {
        private enum Phase { Idle, Reading, Writing, Verifying }
        private volatile Phase _phase;
        private readonly Func<byte[], bool> _send;
        private readonly Func<long> _clock;
        private long _started;
        private byte _address;
        private ushort _requested;
        private volatile byte[]? _ownedFrame;
        public bool IsBusy => _phase != Phase.Idle;
        public ushort? ActualPeriod { get; private set; }
        public bool Verified { get; private set; }
        public int Epoch { get; private set; }
        public string Status { get; private set; } = "尚未读取待机周期";

        /// <summary>发送前校验失败也保留为状态源，避免周期渲染覆盖错误提示。</summary>
        public void ReportRejection(string reason) => Status = reason;

        public StandbyPeriodSession(Func<byte[], bool> send, Func<long>? clock = null)
        {
            _send = send;
            _clock = clock ?? (() => Environment.TickCount64);
        }

        public bool Blocks(byte[] frame)
        {
            if (!IsBusy || ReferenceEquals(frame, _ownedFrame)) return false;
            // 安全停机与归还BMS轮询始终放行；其余请求不进入固件单槽等待区。
            if (frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                (frame[3] == 0x09 || (frame.Length == 8 && frame[3] == 0x0D && frame[5] == 1))) return false;
            return true;
        }

        public bool Read(byte address) => Begin(address, 0, Phase.Reading);
        public bool Write(byte address, ushort seconds)
        {
            if (seconds == 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            return Begin(address, seconds, Phase.Writing);
        }

        private bool Begin(byte address, ushort seconds, Phase phase)
        {
            if (IsBusy) return false;
            Epoch++;
            _address = address;
            _requested = seconds;
            ActualPeriod = null;
            Verified = false;
            _phase = phase;
            Status = phase == Phase.Writing ? "等待FCU保存待机周期" : "等待待机周期回读";
            return SendOwned(phase == Phase.Writing ? FirmwareMainProtocol.WriteStandbyPeriod(address, seconds)
                : FirmwareMainProtocol.ReadStandbyPeriod(address));
        }

        private bool SendOwned(byte[] frame)
        {
            _started = _clock();
            _ownedFrame = frame;
            try
            {
                if (_send(frame)) return true;
                Finish("发送失败，设备结果未知；请重新读取核对");
                return false;
            }
            finally { _ownedFrame = null; }
        }

        public bool Receive(byte[] frame)
        {
            if (!IsBusy) return false;
            if (_phase == Phase.Writing && FirmwareMainProtocol.IsResponse(frame, _address, 0x82, 1))
            {
                byte result = frame[5];
                if (result != 0)
                {
                    string reason = result == 3 ? "设备忙（仅OFF可写）" : result == 9 ? "保存失败（固件已回滚）" :
                        ProtocolConstants.GetErrorCodeDescription(result);
                    Finish($"待机周期写入失败：{reason}");
                }
                else
                {
                    _phase = Phase.Verifying;
                    Status = "FCU报告保存成功，正在回读核对";
                    SendOwned(FirmwareMainProtocol.ReadStandbyPeriod(_address));
                }
                return true;
            }
            if (_phase is Phase.Reading or Phase.Verifying && frame.Length >= 10 && frame[2] == _address &&
                frame[3] == 0x8A && frame[5] == 9 && frame[6] == 2)
            {
                if (!FirmwareMainProtocol.TryParseStandbyPeriod(frame, _address, out ushort seconds))
                {
                    // 旧固件回空参数域；损坏CRC不终止等待，仍由超时明确反馈。
                    if (FirmwareMainProtocol.IsResponse(frame, _address, 0x8A, 3))
                    { Finish("固件不支持待机周期0x0902，输入未改变"); return true; }
                    return false;
                }
                ActualPeriod = seconds;
                Verified = _phase == Phase.Verifying && seconds == _requested;
                Finish(_phase == Phase.Reading ? $"待机周期读取成功：{seconds}秒" : Verified
                    ? $"待机周期写入并回读一致：{seconds}秒，已保存并重新计时"
                    : $"待机周期回读不一致：请求{_requested}秒，设备{seconds}秒；请重新核对");
                return true;
            }
            return false;
        }

        public void Tick()
        {
            if (IsBusy && _clock() - _started >= 3000)
                Finish("待机周期响应超时，设备结果未知；请检查固件支持并重新读取核对");
        }

        public void Reset()
        {
            Epoch++;
            if (IsBusy) Finish("连接或操作模式已改变，待机参数操作中断，设备结果未知；请重新读取");
            else Status = "尚未读取当前设备待机周期";
            ActualPeriod = null;
            Verified = false;
        }

        private void Finish(string message) { _phase = Phase.Idle; Status = message; }
    }
}
