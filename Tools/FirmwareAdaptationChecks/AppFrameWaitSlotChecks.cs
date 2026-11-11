using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

/// <summary>
/// AppFrameWaitSlot 离线检查。
/// [核查问题3修复] 核心语义: 收帧入口按事务过滤器匹配, 心跳/主动上报/他机应答
/// 不入槽、不覆盖已收应答; 事务结束后的帧不累积。
/// </summary>
internal static class AppFrameWaitSlotChecks
{
    public static void Run()
    {
        var slot = new AppFrameWaitSlot();
        // 与升级页同构的帧: AA 55 addr fn len ... CRC (此处槽不校验 CRC, 由 ProtocolHandler 链路负责)
        byte[] Resp(byte addr, byte fn) => ProtocolHandler.BuildRequestFrame(addr, fn, new byte[] { 0 });
        byte[] heartbeat = Resp(1, 0x48);

        // 1. 无事务时帧被丢弃 (无人等待, 不累积)
        slot.Offer(Resp(1, 0x87));
        Program.Check(slot.WaitOne(30) == null, "frame outside transaction dropped");

        // 2. 非本事务帧 (心跳) 不入槽 → 等待超时 (旧实现会被覆盖丢失目标应答的根源)
        slot.Begin(f => f.Length > 4 && f[3] == 0x87);
        slot.Offer(heartbeat);
        Program.Check(slot.WaitOne(30) == null, "heartbeat never occupies slot");

        // 3. 目标应答入槽并被取走; 取走即清槽
        slot.Offer(Resp(1, 0x87));
        var taken = slot.WaitOne(0);
        Program.Check(taken != null && taken[3] == 0x87, "matching frame taken");
        Program.Check(slot.WaitOne(0) == null, "take clears slot");

        // 4. 关键: 干扰帧不能覆盖已收应答 (先到心跳被过滤, 后到应答完好)
        slot.Offer(heartbeat);
        slot.Offer(Resp(1, 0x87));
        var kept = slot.WaitOne(0);
        Program.Check(kept != null && kept[3] == 0x87, "junk frame cannot displace target answer");

        // 5. 地址核对 (升级页 RequestAppFrame 过滤谓词语义): 他机同功能码应答不入槽
        slot.End();
        byte address = 1;
        slot.Begin(f => f.Length > 4 && f[3] == 0x87 && f[2] == address);
        slot.Offer(Resp(2, 0x87));     // 地址 2 的 0x87
        Program.Check(slot.WaitOne(30) == null, "foreign-address answer rejected");
        slot.Offer(Resp(1, 0x87));
        var own = slot.WaitOne(0);
        Program.Check(own != null && own[2] == 1, "own-address answer accepted");

        // 6. End 撤销过滤器: 后续帧不再入槽
        slot.End();
        slot.Offer(Resp(1, 0x87));
        Program.Check(slot.WaitOne(30) == null, "frame after End dropped");

        // 7. Begin 清空上一事务遗留帧 (先清后发序)
        slot.Begin(f => f[3] == 0x87);
        slot.Offer(Resp(1, 0x87));
        slot.Begin(f => f[3] == 0x81);   // 新事务: 旧 0x87 遗留必须被清
        Program.Check(slot.WaitOne(30) == null, "Begin clears stale frame from previous transaction");
        slot.End();

        // 8. 与 EnterBootloaderSession 集成: 0x92 过滤器 + 心跳干扰, 拒绝应答不丢失
        long now = 100;
        var sent = new List<byte[]>();
        var session = new EnterBootloaderSession(f => { sent.Add(f); return true; }, () => now);
        var slot2 = new AppFrameWaitSlot();
        slot2.Begin(f => f.Length == ProtocolConstants.MIN_FRAME_LENGTH + 1 &&
            f[3] == ProtocolConstants.RESP_ENTER_BOOTLOADER);
        Program.Check(session.Request(1), "enter bl request sent");
        // 模拟 RX 线程: 心跳先到被过滤, 0x92/0x03 拒绝应答随后到达
        slot2.Offer(Resp(1, 0x48));
        slot2.Offer(ProtocolHandler.BuildRequestFrame(1, 0x92, new byte[] { 0x03 }));
        var frame = slot2.WaitOne(0);
        Program.Check(frame != null && session.Receive(frame) && session.OutcomeValue == EnterBootloaderSession.Outcome.Rejected,
            "rejection survives heartbeat interference (audit issue 3 repro now clean)");
        slot2.End();
    }
}
