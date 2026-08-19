using System;
using System.Threading;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// Bootloader 升级命令客户端
    /// 接线模型: SerialPortManager.DataReceived(线程池线程) → FeedByte 逐字节喂入;
    /// 命令方法为阻塞同步调用, 仅限后台线程(Task.Run)调用, 由 FirmwareUpgradeView 驱动。
    /// 创建日期: 2026-08-19
    /// 依据: docs/03-设计方案/FCU-RS485-Bootloader在线升级设计方案.md §4.1/§4.4
    /// </summary>
    public class BlUpgradeClient
    {
        /// <summary>命令执行结果 (Ok=Status 0x00; Status 0xFF 表示超时/帧损坏)</summary>
        public sealed record BlResult(bool Ok, byte Status, byte[]? Data);

        private const byte STATUS_TIMEOUT = 0xFF;

        private enum RxState { Idle, H0, H1, Cmd, LenL, LenH, Data, CkL, CkH }

        private readonly object _lock = new();
        private readonly byte[] _rxBuf = new byte[7 + BlProtocolHelper.WRITE_DATA_MAX];

        private RxState _state = RxState.Idle;
        private int _rxIdx;
        private ushort _rxLen;

        private bool _respReceived;
        private byte _respStatus;
        private byte[]? _respData;

        private readonly Managers.SerialPortManager _serial;

        public BlUpgradeClient(Managers.SerialPortManager serial)
        {
            _serial = serial ?? throw new ArgumentNullException(nameof(serial));
        }

        /// <summary>复位接收状态机与挂起应答 (进入/退出升级模式、波特率切换后调用)</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _state = RxState.Idle;
                _rxIdx = 0;
                _rxLen = 0;
                _respReceived = false;
                _respData = null;
                _respStatus = STATUS_TIMEOUT;
            }
        }

        /// <summary>
        /// 串口字节喂入 (DataReceived 线程池线程调用)
        /// </summary>
        public void FeedByte(byte b)
        {

            lock (_lock)
            {
                switch (_state)
                {
                    case RxState.Idle:
                        if (b == 0xAA) { _rxBuf[0] = b; _state = RxState.H0; }
                        break;

                    case RxState.H0:
                        if (b == 0x55) { _rxBuf[1] = b; _state = RxState.Cmd; }
                        else if (b != 0xAA) _state = RxState.Idle;
                        break;

                    case RxState.Cmd:
                        _rxBuf[2] = b;
                        _state = RxState.LenL;
                        break;

                    case RxState.LenL:
                        _rxBuf[3] = b;
                        _rxLen = b;
                        _state = RxState.LenH;
                        break;

                    case RxState.LenH:
                        _rxBuf[4] = b;
                        _rxLen = (ushort)(_rxLen | (b << 8));
                        /* BL 侧应答数据域最大 12B, 防异常长帧写穿缓冲 */
                        if (_rxLen > 32)
                        {
                            _state = RxState.Idle;
                        }
                        else if (_rxLen == 0)
                        {
                            _rxIdx = 5;
                            _state = RxState.CkL;
                        }
                        else
                        {
                            _rxIdx = 5;
                            _state = RxState.Data;
                        }
                        break;

                    case RxState.Data:
                        _rxBuf[_rxIdx++] = b;
                        if (_rxIdx >= 5 + _rxLen) _state = RxState.CkL;
                        break;

                    case RxState.CkL:
                        _rxBuf[_rxIdx++] = b;
                        _state = RxState.CkH;
                        break;

                    case RxState.CkH:
                        _rxBuf[_rxIdx] = b;
                        _state = RxState.Idle;
                        int total = 5 + _rxLen + 2;
                        if (BlProtocolHelper.TryParseResponse(_rxBuf, total, out var data, out var status))
                        {
                            /* 结构有效即视为应答到达 (含 Status=0x01 显式拒绝, 由上层快速失败) */
                            _respStatus = status;
                            _respData = data ?? Array.Empty<byte>();
                            _respReceived = true;
                            System.Threading.Monitor.Pulse(_lock);
                        }
                        /* 结构失败(校验和/长度): 静默丢弃, 等上位机侧超时重发 */
                        break;

                    default:
                        _state = RxState.Idle;
                        break;
                }
            }
        }

        /// <summary>
        /// 发送命令并等待应答 (阻塞, 后台线程调用)
        /// </summary>
        /// <param name="cmd">命令码</param>
        /// <param name="data">数据域 (可 null)</param>
        /// <param name="timeoutMs">单次应答超时</param>
        /// <param name="maxAttempts">最大尝试次数 (仅超时/帧损坏重试; BL 显式拒绝 Status=0x01 不重试)</param>
        public BlResult Request(byte cmd, byte[]? data, int timeoutMs, int maxAttempts = 1)
        {
            byte[] frame = BlProtocolHelper.BuildRequestFrame(cmd, data);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                lock (_lock)
                {
                    /* 每次尝试前复位接收状态机: 清除可能的残帧(噪声/上次超时的迟到半帧),
                     * 避免残帧吞掉本档重试额度 [批次2审查 P2] */
                    _state = RxState.Idle;
                    _rxIdx = 0;
                    _rxLen = 0;
                    _respReceived = false;
                    _respData = null;
                    _respStatus = STATUS_TIMEOUT;
                }

                if (!_serial.SendData(frame))
                {
                    /* 发送瞬时失败(如驱动忙)不立即放弃, 走下一档重试 [批次2审查 P2] */
                    if (attempt >= maxAttempts) return new BlResult(false, STATUS_TIMEOUT, null);
                    Thread.Sleep(50);
                    continue;
                }

                var deadline = Environment.TickCount64 + timeoutMs;
                bool got = false;
                lock (_lock)
                {
                    while (!_respReceived)
                    {
                        long remain = deadline - Environment.TickCount64;
                        if (remain <= 0) break;
                        got = Monitor.Wait(_lock, (int)remain);
                        if (got) break;
                    }

                    if (_respReceived)
                    {
                        byte st = _respStatus;
                        byte[]? payload = _respData;
                        /* BL 显式拒绝(0x01): 快速失败, 不消耗重试额度 [设计 §4.4 S5] */
                        return new BlResult(st == BlProtocolHelper.Status.Ok, st, payload);
                    }
                }
                /* 超时 → 重试 */
            }

            return new BlResult(false, STATUS_TIMEOUT, null);
        }

        /* ---------------- 高层命令封装 (超时分档见 [设计 §4.4]) ---------------- */

        public BlResult GetUid()
            => Request(BlProtocolHelper.Commands.GetUid, null, 2000);

        public BlResult GetVersion()
            => Request(BlProtocolHelper.Commands.GetVersion, null, 2000);

        public BlResult EraseApp()
            => Request(BlProtocolHelper.Commands.EraseApp, null, 5000, maxAttempts: 2);

        public BlResult WriteChunk(uint address, byte[] chunk)
            => Request(BlProtocolHelper.Commands.Write,
                       BlProtocolHelper.BuildWriteData(address, chunk), 2000, maxAttempts: 3);

        public BlResult WriteMeta(byte[] metaData16)
            => Request(BlProtocolHelper.Commands.WriteMeta, metaData16, 5000);

        public BlResult Verify(uint length)
        {
            var n = new byte[4];
            n[0] = (byte)(length & 0xFF);
            n[1] = (byte)((length >> 8) & 0xFF);
            n[2] = (byte)((length >> 16) & 0xFF);
            n[3] = (byte)((length >> 24) & 0xFF);
            return Request(BlProtocolHelper.Commands.Verify, n, 2000);
        }

        /// <summary>0x05 设备复位命令 (与状态机复位的 Reset() 重名, 故名 ResetDevice)</summary>
        public BlResult ResetDevice()
            => Request(BlProtocolHelper.Commands.Reset, null, 2000);
    }
}
