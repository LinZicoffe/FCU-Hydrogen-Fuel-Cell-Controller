using System;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// 软件进入 Bootloader 升级请求会话（待测版固件 450V_FCU_NewPCB@99aedb7 + Boot-For-FCU@169cdf0）。
    /// 依据: App/Src/fcu_protocol.c fcu_handle_enter_bootloader() —— 单播 0x12 + 确认字节 0x01，
    /// 应答 0x92 数据域 1B 错误码 (0x00 接受 / 0x02 确认值错误 / 0x03 非OFF或设备忙)；
    /// 固件先发应答，发送完成后写 TAMP 一次性标记并软复位 (fcu_boot.c)，
    /// BL 校验标记(软复位来源+双字含反码)后常驻等待 XCP，不依赖普通上电 3 秒窗口 (fcu_bl.c)。
    /// 单次发送不重试：固件接受后即复位，重发只会再次触发标记写入。
    /// 应答严格核对地址/功能码/长度/错误码 (CRC 已由 ProtocolHandler 完整帧链路校验)，
    /// 非本事务帧 (心跳/主动上报/他机应答/畸形帧) 一律丢弃继续等待。
    /// 线程模型: 升级流程后台线程单线程驱动 (Request/Tick/Receive/RemainMs 均由同一调用方线程调用,
    /// 帧经 _appFrameWait 锁内暂存转交), 可变状态不跨线程共享; 时钟/发送可注入。创建日期: 2026-11-10
    /// </summary>
    public sealed class EnterBootloaderSession
    {
        /// <summary>会话终态</summary>
        public enum Outcome : byte
        {
            /// <summary>尚未发送或在等待中</summary>
            None = 0,
            /// <summary>0x92/0x00：设备已接受，即将复位进入 Bootloader</summary>
            Accepted = 1,
            /// <summary>0x92/非0：设备显式拒绝 (ErrorCode 携带固件错误码)</summary>
            Rejected = 2,
            /// <summary>请求发送失败 (串口发送未成功)</summary>
            SendFailed = 3,
            /// <summary>等待应答超时 (旧固件无 0x12 处理 / 应答丢失)</summary>
            Timeout = 4
        }

        private readonly Func<byte[], bool> _send;
        private readonly Func<long> _clock;
        private byte _address;
        private long _requestedAt;

        /// <summary>是否仍在等待 0x92 应答</summary>
        public bool Waiting { get; private set; }

        /// <summary>会话是否已进入终态 (Accepted/Rejected/SendFailed/Timeout)</summary>
        public bool Terminal => OutcomeValue != Outcome.None;

        /// <summary>终态结果 (非终态时为 None)</summary>
        public Outcome OutcomeValue { get; private set; } = Outcome.None;

        /// <summary>终态为 Rejected 时携带固件错误码，其余为 0</summary>
        public byte ErrorCode { get; private set; }

        /// <summary>当前状态中文描述 (升级页日志/提示用)</summary>
        public string Status { get; private set; } = "未发送";

        /// <summary>距应答超时剩余毫秒 (未在等待时为 0)</summary>
        public int RemainMs => Waiting
            ? (int)Math.Max(0, _requestedAt + ProtocolConstants.ENTER_BL_ACK_TIMEOUT_MS - _clock())
            : 0;

        public EnterBootloaderSession(Func<byte[], bool> send, Func<long>? clock = null)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _clock = clock ?? (() => Environment.TickCount64);
        }

        /// <summary>构建 0x12 请求帧 (复用 ProtocolHandler 组帧与 CRC，设备地址变化自动重算)</summary>
        public static byte[] BuildRequest(byte address) =>
            ProtocolHandler.BuildRequestFrame(address, ProtocolConstants.FUNC_ENTER_BOOTLOADER,
                new byte[] { ProtocolConstants.ENTER_BL_CONFIRM });

        /// <summary>发送升级请求。等待应答期间重复调用被拒绝（不重发，固件接受后已复位）。
        /// 返回 false 表示发送失败或重复调用，终态经 OutcomeValue 区分。</summary>
        public bool Request(byte address)
        {
            if (Waiting) return false;
            _address = address;
            ErrorCode = 0;
            OutcomeValue = Outcome.None;
            if (!_send(BuildRequest(address)))
            {
                OutcomeValue = Outcome.SendFailed;
                Status = "升级请求发送失败 (串口)";
                return false;
            }
            Waiting = true;
            _requestedAt = _clock();
            Status = "等待升级请求应答 (0x92)";
            return true;
        }

        /// <summary>喂入一条完整 AA55 帧 (CRC 已由 ProtocolHandler 完整帧链路校验)。
        /// 严格核对地址/功能码/长度后读取错误码判定终态；不匹配的帧返回 false 并继续等待。
        /// 返回 true 表示本帧使会话进入终态 (Accepted/Rejected)。</summary>
        public bool Receive(byte[] frame)
        {
            if (!Waiting) return false;
            if (frame.Length != ProtocolConstants.MIN_FRAME_LENGTH + 1 ||
                frame[2] != _address ||
                frame[3] != ProtocolConstants.RESP_ENTER_BOOTLOADER ||
                frame[4] != 1)
                return false;   /* 心跳/主动上报/他机应答/畸形 0x92：非本事务帧，丢弃继续等待 */

            Waiting = false;
            ErrorCode = frame[5];
            OutcomeValue = ErrorCode == ProtocolConstants.ERR_SUCCESS ? Outcome.Accepted : Outcome.Rejected;
            Status = OutcomeValue == Outcome.Accepted
                ? "设备已接受升级请求，复位进入 Bootloader"
                : $"设备拒绝进入升级 (错误码 0x{ErrorCode:X2})";
            return true;
        }

        /// <summary>推进时钟判定等待超时 (与 BmsCacheSession.Tick 同款驱动方式)</summary>
        public void Tick()
        {
            if (Waiting && _clock() - _requestedAt >= ProtocolConstants.ENTER_BL_ACK_TIMEOUT_MS)
            {
                Waiting = false;
                OutcomeValue = Outcome.Timeout;
                Status = "等待升级请求应答超时 (设备可能为不支持软件进入的旧固件)";
            }
        }

        /// <summary>复位会话 (断开/模式切换/异常退出时清理，供下一次全新握手)</summary>
        public void Reset()
        {
            Waiting = false;
            OutcomeValue = Outcome.None;
            ErrorCode = 0;
            Status = "未发送";
        }
    }
}
