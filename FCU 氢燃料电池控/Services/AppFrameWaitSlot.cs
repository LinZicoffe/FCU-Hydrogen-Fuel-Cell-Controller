using System;
using System.Threading;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// AA55 应答事务等待槽 (升级页前置检查 / 软件进入 Bootloader 握手共用)。
    /// [核查问题3修复] 旧实现是"每条完整帧覆盖单槽": 心跳 (0x4842)/主动上报 (0xF0/0xF1/0xF2)/
    /// 他机应答若落在"目标应答入槽→等待线程取走"窗口内会覆盖目标应答, 造成假超时——
    /// 前置检查 3 次用尽误入"APP 不在线"分支、0x92 拒绝原因丢失报成泛化超时。
    /// 本类在收帧入口按当前事务过滤器 (功能码 + 设备地址) 就地匹配:
    /// 非本事务帧一律不入槽, 从机制上消除覆盖丢帧; 事务外的帧直接丢弃 (无等待者时无人消费)。
    /// 线程模型: Offer 由串口接收线程调用; Begin/End/WaitOne 由升级后台线程单线程驱动
    /// (一次只存在一个等待事务), 与 EnterBootloaderSession/BmsCacheSession 的会话封闭口径一致。
    /// 创建日期: 2026-11-11
    /// </summary>
    public sealed class AppFrameWaitSlot
    {
        private readonly object _gate = new();
        private byte[]? _pending;
        private Func<byte[], bool>? _filter;    /* 当前事务过滤器, null=无事务 */

        /// <summary>开始一次等待事务: 清空历史帧并登记过滤器 (发送请求之前调用, 对齐"先清后发"序)。</summary>
        public void Begin(Func<byte[], bool> filter)
        {
            lock (_gate)
            {
                _pending = null;
                _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            }
        }

        /// <summary>结束等待事务 (所有退出路径必须调用, 建议 finally): 撤销过滤器并丢弃未取走的帧。</summary>
        public void End()
        {
            lock (_gate)
            {
                _filter = null;
                _pending = null;
            }
        }

        /// <summary>收帧入口 (RX 线程): 事务在位时仅过滤器放行的帧入槽并唤醒等待方; 其余帧不入槽不覆盖。</summary>
        public void Offer(byte[] frame)
        {
            lock (_gate)
            {
                var filter = _filter;
                if (filter == null || !filter(frame)) return;
                _pending = frame;
                Monitor.Pulse(_gate);
            }
        }

        /// <summary>等待并取走一条已匹配帧: timeoutMs 内无帧返回 null; 取走即清槽。
        /// 调用方在循环中自行处理取消与剩余时间 (对齐旧 RequestAppFrame/AwaitEnterBootloaderAck 等待口径)。</summary>
        public byte[]? WaitOne(int timeoutMs)
        {
            lock (_gate)
            {
                if (_pending == null && timeoutMs > 0)
                    Monitor.Wait(_gate, timeoutMs);
                var frame = _pending;
                _pending = null;
                return frame;
            }
        }
    }
}
