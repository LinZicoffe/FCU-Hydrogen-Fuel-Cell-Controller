using System;
using System.Threading;
using FCU_氢燃料电池控.Managers;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// OpenBLT XCP-over-RS485 主机端命令客户端
    /// 接线模型: SerialPortManager.DataReceived(线程池线程) → FeedByte 逐字节喂入;
    /// 命令方法为阻塞同步调用, 仅限后台线程(Task.Run)调用, 由 FirmwareUpgradeView 驱动。
    /// 创建日期: 2026-09-21
    /// 依据: docs/实施步骤-OpenBLT升级与固件适配.md §4.1, 逐条对照 Boot-For-FCU 源码:
    ///   - 帧格式 [len:1B][payload]: Source/ARMCM4_STM32G4/rs232.c:150-308 (无校验字节, plausibility.h:258 CS_TYPE=0)
    ///   - len∈[1,129]: rs232.c:244-245 + plausibility.h:93/97 (CTO 收发均 129B)
    ///   - 字节序 Intel 小端: blt_conf.h:51 MOTOROLA=0
    ///   - 命令码/响应/错误码: Source/xcp.h:142-204, Source/xcp.c 各 XcpCmd* 处理函数
    ///   - BL 侧帧间隙超时 200ms: rs232.c:49 (主机侧镜像值)
    /// </summary>
    public class OpenBltXcpClient
    {
        /// <summary>命令执行结果 (Ok=响应首字节 0xFF 且长度符合; ErrorCode 仅在 0xFE 错误响应时有效)</summary>
        public sealed record XcpResult(bool Ok, bool Timeout, byte ErrorCode, byte[] Data);

        private readonly object _lock = new();
        /* +1: [0] 存 len 字节, payload 自 [1] 起 → 容纳 len=129 的满帧需 130B (Upload(128) 场景) */
        private readonly byte[] _rxBuf = new byte[ProtocolConstants.XCP_MAX_CTO + 1];

        private bool _collecting;            /* 正在收一帧(len 已读, payload 收集中) */
        private int _rxLen;                  /* 当前帧 payload 长度 */
        private int _rxIdx;
        private long _frameStartTick;        /* 帧首字节时刻, 用于 200ms 间隙超时丢弃半帧 */

        private volatile bool _respReceived;
        private byte[] _respPayload = Array.Empty<byte>();
        private int _expectedLen = -1;       /* 当前命令期待的应答 payload 长度, -1 不校验 */

        private readonly Func<byte[], bool> _send;

        public OpenBltXcpClient(SerialPortManager serial)
            : this(serial != null ? f => serial.SendData(f)
                  : throw new ArgumentNullException(nameof(serial)))
        {
        }

        /// <summary>注入构造: 直接提供发送委托, 不依赖串口管理器 (对齐会话类 send 注入模式, 供离线检查使用)。</summary>
        public OpenBltXcpClient(Func<byte[], bool> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>SYNCH 同步屏障通过判据: 已连接的 BL 对 SYNCH 回 [FE 00] (err=0, 2B)——该应答本身
        /// Ok=false, 不能用 Ok 判定; 在途迟到的单字节 0xFF 成功应答因长度不符被丢弃, 无法冒充屏障
        /// (xcp.c:302-306; 非零错误码的 [FE xx] 亦不算, 防旧命令错误应答冒充)。</summary>
        public static bool IsSynchPositive(XcpResult result) =>
            !result.Timeout && result.Data.Length == 2 &&
            result.Data[0] == ProtocolConstants.XCP_PID_ERR && result.Data[1] == 0;

        /// <summary>复位接收状态机与挂起应答 (进入/退出升级模式、波特率切换后调用, 对齐 BlUpgradeClient.Reset 先例)</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _collecting = false;
                _rxIdx = 0;
                _rxLen = 0;
                _respReceived = false;
                _respPayload = Array.Empty<byte>();
                _expectedLen = -1;
            }
        }

        /// <summary>
        /// 串口字节喂入 (DataReceived 线程池线程调用)
        /// </summary>
        public void FeedByte(byte b)
        {
            lock (_lock)
            {
                if (_collecting && Environment.TickCount64 - _frameStartTick > ProtocolConstants.XCP_FRAME_GAP_TIMEOUT_MS)
                {
                    /* 整帧须在首字节后 200ms 内完成, 超时半帧作废, 当前字节按新帧 len 重新判定
                     * (忠实镜像 rs232.c:248/297: xcpCtoRxStartTime 仅在首字节置位) */
                    _collecting = false;
                }

                if (!_collecting)
                {
                    if (b >= 1 && b <= ProtocolConstants.XCP_MAX_CTO)
                    {
                        _rxLen = b;
                        _rxBuf[0] = b;
                        _rxIdx = 1;
                        _frameStartTick = Environment.TickCount64;
                        _collecting = true;
                    }
                    /* len=0 或 >129 的孤立字节: 丢弃 (rs232.c:244 同样拒绝) */
                }
                else
                {
                    _rxBuf[_rxIdx++] = b;
                    if (_rxIdx > _rxLen)
                    {
                        /* payload 收满(含 len 字节共 _rxLen+1) */
                        byte[] payload = new byte[_rxLen];
                        Array.Copy(_rxBuf, 1, payload, 0, _rxLen);
                        _collecting = false;

                        /* 错误帧 [FE errCode] 恒为 2B, 不受当前命令 expectedLen 约束——
                         * 否则 expectedLen=1 的命令(SetMta/Program* 等)会把显式错误当迟到帧丢弃、误报超时 */
                        if (_expectedLen < 0 || _rxLen == _expectedLen ||
                            (_rxLen >= 2 && payload[0] == ProtocolConstants.XCP_PID_ERR))
                        {
                            _respPayload = payload;
                            _respReceived = true;
                            Monitor.Pulse(_lock);
                        }
                        /* 长度与当前命令期待不符的迟到帧: 丢弃继续等 */
                    }
                }
            }
        }

        /// <summary>
        /// 发送 XCP 命令并等待应答 (阻塞, 后台线程调用)
        /// </summary>
        /// <param name="payload">命令字节 (首字节为命令码)</param>
        /// <param name="timeoutMs">单次应答超时</param>
        /// <param name="expectedLen">期待应答 payload 长度: -1 不校验</param>
        /// <param name="allowNoResponse">超时亦视为成功 (PROGRAM_RESET 场景: BL 直接跳 APP 可能不回帧, xcp.c:1562-1579)</param>
        public XcpResult Request(byte[] payload, int timeoutMs, int expectedLen = -1, bool allowNoResponse = false)
        {
            if (payload.Length == 0 || payload.Length > ProtocolConstants.XCP_MAX_CTO)
                throw new ArgumentOutOfRangeException(nameof(payload), $"XCP 命令长度 {payload.Length} 超出 [1,{ProtocolConstants.XCP_MAX_CTO}]");

            var frame = new byte[payload.Length + 1];
            frame[0] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 1, payload.Length);

            lock (_lock)
            {
                /* 发送前复位接收状态: 清除残帧与陈旧应答 (对齐 BlUpgradeClient.Request 模式) */
                _collecting = false;
                _rxIdx = 0;
                _rxLen = 0;
                _respReceived = false;
                _respPayload = Array.Empty<byte>();
                _expectedLen = expectedLen;
            }

            if (!_send(frame))
                /* 发送瞬时失败(串口未开/驱动忙)同样以 Timeout 标记返回失败; 命令级重试由上层
                 * 升级流程的写入循环负责(对齐旧 FirmwareUpgradeView S5 每块 3 次重试模式) */
                return new XcpResult(false, true, 0, Array.Empty<byte>());

            var deadline = Environment.TickCount64 + timeoutMs;
            lock (_lock)
            {
                while (!_respReceived)
                {
                    long remain = deadline - Environment.TickCount64;
                    if (remain <= 0) break;
                    Monitor.Wait(_lock, (int)remain);
                }

                if (_respReceived)
                {
                    byte[] resp = _respPayload;
                    if (resp.Length > 0 && resp[0] == ProtocolConstants.XCP_PID_RES)
                        return new XcpResult(true, false, 0, resp);
                    if (resp.Length >= 2 && resp[0] == ProtocolConstants.XCP_PID_ERR)
                        return new XcpResult(false, false, resp[1], resp);   /* BL 显式错误, 不重试由上层决定 */
                    return new XcpResult(false, false, 0, resp);             /* 首字节既非 0xFF 也非 0xFE: 协议违例 */
                }
            }

            /* 超时 */
            if (allowNoResponse)
                return new XcpResult(true, true, 0, Array.Empty<byte>());
            return new XcpResult(false, true, 0, Array.Empty<byte>());
        }

        /* ---------------- 高层命令封装 (字节布局逐条对照 xcp.c) ---------------- */

        /// <summary>CONNECT [FF 00] → [FF res commMode maxCto maxDtoL maxDtoH ver ver] 8B (xcp.c:552-631)</summary>
        public XcpResult Connect(int timeoutMs = ProtocolConstants.XCP_CONNECT_WAIT_MS)
            => Request(new byte[] { ProtocolConstants.XCP_CMD_CONNECT, 0x00 }, timeoutMs, expectedLen: 8);

        /// <summary>SYNCH [FC] → 已连接时 [FE 00]; 未连接时无响应 (xcp.c:302-306)。
        /// 调用方判定: !Timeout &amp;&amp; Data.Length==2 表示 BL 在线且失步已恢复
        /// (注意正常应答本身就是错误包 [FE 00], 返回 Ok=false——不能用 Ok 判定)。</summary>
        public XcpResult Synch(int timeoutMs = ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS)
            => Request(new byte[] { ProtocolConstants.XCP_CMD_SYNCH }, timeoutMs, expectedLen: 2, allowNoResponse: true);

        /// <summary>SET_MTA [F6 00 00 00 addr(小端4B)] → [FF] (xcp.c:748-758)</summary>
        public XcpResult SetMta(uint address)
            => Request(new byte[] { ProtocolConstants.XCP_CMD_SET_MTA, 0x00, 0x00, 0x00,
                                    (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24) },
                       ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: 1);

        /// <summary>PROGRAM_START [D2] → [FF 00 00 maxCto 00 00 00] 7B (xcp.c:1300-1349)</summary>
        public XcpResult ProgramStart()
            => Request(new byte[] { ProtocolConstants.XCP_CMD_PROGRAM_START },
                       ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: 7);

        /// <summary>PROGRAM_CLEAR [D1 00 00 00 size(小端4B)] → [FF] (xcp.c:1497-1537; BL 按覆盖扇区整擦 flash.c:352-367)</summary>
        public XcpResult ProgramClear(uint size)
            => Request(new byte[] { ProtocolConstants.XCP_CMD_PROGRAM_CLEAR, 0x00, 0x00, 0x00,
                                    (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) },
                       ProtocolConstants.XCP_PROGRAM_CLEAR_TIMEOUT_MS, expectedLen: 1);

        /// <summary>PROGRAM [D0 N data...] (N≤127) → [FF]; N=0 为编程结束触发 NvmDone (xcp.c:1414-1487)</summary>
        public XcpResult Program(byte[] data)
        {
            if (data.Length > ProtocolConstants.XCP_PROGRAM_DATA_MAX)
                throw new ArgumentOutOfRangeException(nameof(data), $"PROGRAM 单块上限 {ProtocolConstants.XCP_PROGRAM_DATA_MAX}");
            var cmd = new byte[2 + data.Length];
            cmd[0] = ProtocolConstants.XCP_CMD_PROGRAM;
            cmd[1] = (byte)data.Length;
            Array.Copy(data, 0, cmd, 2, data.Length);
            return Request(cmd, ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: 1);
        }

        /// <summary>PROGRAM_MAX [C9 data×128] (定长 128B) → [FF] (xcp.c:1359-1404, programLen=XCP_CTO_PACKET_LEN-1)</summary>
        public XcpResult ProgramMax(byte[] data)
        {
            if (data.Length != ProtocolConstants.XCP_PROGRAM_MAX_DATA)
                throw new ArgumentOutOfRangeException(nameof(data), $"PROGRAM_MAX 必须恰为 {ProtocolConstants.XCP_PROGRAM_MAX_DATA} 字节");
            var cmd = new byte[1 + data.Length];
            cmd[0] = ProtocolConstants.XCP_CMD_PROGRAM_MAX;
            Array.Copy(data, 0, cmd, 1, data.Length);
            return Request(cmd, ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: 1);
        }

        /// <summary>PROGRAM_RESET [CF] → [FF] 或无响应(BL 直接启动新 APP, xcp.c:1547-1580)</summary>
        public XcpResult ProgramReset()
            => Request(new byte[] { ProtocolConstants.XCP_CMD_PROGRAM_RESET },
                       ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: 1, allowNoResponse: true);

        /// <summary>UPLOAD [F5 n] (n≤128) → [FF data×n], MTA 自动后增 (xcp.c:768-824; 联调读回校验用)</summary>
        public XcpResult Upload(byte n)
        {
            if (n < 1 || n > ProtocolConstants.XCP_PROGRAM_MAX_DATA)
                throw new ArgumentOutOfRangeException(nameof(n), $"UPLOAD 长度必须在 [1,{ProtocolConstants.XCP_PROGRAM_MAX_DATA}] (上界对齐 xcp.c:774; 下界 1 为主机侧自定, BL 允许 n=0 回裸[FF])");
            return Request(new byte[] { ProtocolConstants.XCP_CMD_UPLOAD, n },
                           ProtocolConstants.XCP_RESPONSE_TIMEOUT_MS, expectedLen: n + 1);
        }
    }
}
