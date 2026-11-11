using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

internal static class EnterBootloaderChecks
{
    /// <summary>固件协议文档示例帧 (450V_FCU_NewPCB@99aedb7 配套说明, CRC 已独立复算冻结)：
    /// 请求 AA 55 01 12 01 01 27 F8 / 成功应答 AA 55 01 92 01 00 0C 83。</summary>
    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));

    private static byte[] Resp(byte address, byte errorCode) =>
        ProtocolHandler.BuildRequestFrame(address, ProtocolConstants.RESP_ENTER_BOOTLOADER, new[] { errorCode });

    public static void Run()
    {
        long now = 100;
        var sent = new List<byte[]>();
        var session = new EnterBootloaderSession(frame => { sent.Add(frame); return true; }, () => now);

        /* ---- 请求字节: 固件文档金样帧 (组帧/CRC 复用 ProtocolHandler, 地址参与 CRC) ---- */
        Program.Check(session.Request(1) && session.Waiting, "enter BL request sent, waiting");
        Program.Check(sent.Count == 1 && sent[0].SequenceEqual(Hex("AA550112010127F8")), "request matches firmware doc golden frame");
        Program.Check(sent[0][3] == ProtocolConstants.FUNC_ENTER_BOOTLOADER && sent[0][4] == 1 && sent[0][5] == ProtocolConstants.ENTER_BL_CONFIRM,
            "request is unicast 0x12 + confirm 0x01, never legacy 0x11");
        Program.Check(!session.Request(1) && sent.Count == 1, "no resend while waiting (device resets after accept)");

        /* ---- 地址变化重新计算 CRC (复用组帧实现) ---- */
        var other = EnterBootloaderSession.BuildRequest(2);
        Program.Check(other[2] == 2 && !other[..^2].SequenceEqual(sent[0][..^2]) && !other[^2..].SequenceEqual(sent[0][^2..]),
            "address change recomputes frame CRC");

        /* ---- 应答严格核对: 非本事务帧一律丢弃继续等待, 绝不误判 ---- */
        Program.Check(!session.Receive(Resp(2, 0)) && session.Waiting, "wrong address 0x92 cannot finish");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x91, new byte[18])) && session.Waiting,
            "BMS cache 0x91 not consumed by enter BL wait");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x92, new byte[] { 0, 0 })) && session.Waiting,
            "malformed 0x92 (len 2) rejected");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x92, Array.Empty<byte>())) && session.Waiting,
            "malformed 0x92 (len 0) rejected");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x87, new byte[ProtocolConstants.STACK_INFO_DATA_LENGTH])) && session.Waiting,
            "unrelated 0x87 response ignored");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0xF1, new byte[] { 1 })) && session.Waiting, "active report ignored");

        /* ---- 成功应答: 固件文档金样帧 (CRC 由完整帧链路校验, 此处直喂会话验证语义) ---- */
        Program.Check(session.Receive(Hex("AA55019201000C83")) && session.Terminal && !session.Waiting, "golden 0x92/00 accepted");
        Program.Check(session.OutcomeValue == EnterBootloaderSession.Outcome.Accepted && session.ErrorCode == 0 &&
            session.Status.Contains("接受"), "accepted outcome and status");
        Program.Check(!session.Receive(Resp(1, 3)), "terminal ignores further frames");

        /* ---- 显式拒绝错误码 (固件 0x02 确认值错误 / 0x03 非OFF或设备忙) ---- */
        foreach (byte errorCode in new byte[] { 0x02, 0x03 })
        {
            session.Reset();
            session.Request(1);
            Program.Check(session.Receive(Resp(1, errorCode)) && session.Terminal &&
                session.OutcomeValue == EnterBootloaderSession.Outcome.Rejected && session.ErrorCode == errorCode,
                $"explicit reject 0x{errorCode:X2} surfaces firmware error code");
        }

        /* ---- 等待超时: 旧固件无 0x12 处理不应答, 超时不当作成功 ---- */
        session.Reset();
        session.Request(1);
        now += ProtocolConstants.ENTER_BL_ACK_TIMEOUT_MS - 1; session.Tick();
        Program.Check(session.Waiting && !session.Terminal, "just before ack timeout still waiting");
        now++; session.Tick();
        Program.Check(!session.Waiting && session.Terminal && session.OutcomeValue == EnterBootloaderSession.Outcome.Timeout &&
            session.Status.Contains("旧固件"), "ack timeout terminal, never accepted");
        Program.Check(!session.Receive(Hex("AA55019201000C83")) && session.OutcomeValue == EnterBootloaderSession.Outcome.Timeout,
            "late golden response cannot resurrect timed-out session");

        /* ---- 发送失败: 立即终态, 不进入等待 ---- */
        var failed = new EnterBootloaderSession(_ => false, () => now);
        Program.Check(!failed.Request(1) && !failed.Waiting && failed.Terminal &&
            failed.OutcomeValue == EnterBootloaderSession.Outcome.SendFailed, "send failure terminal immediately");

        /* ---- 完整帧链路集成: 分片喂入 ProtocolHandler, CRC 错误帧不会到达会话 ---- */
        var chained = new EnterBootloaderSession(_ => true, () => now);
        chained.Request(1);
        var parser = new ProtocolHandler(); byte[]? parsed = null;
        parser.FrameReceived += f => parsed = f;
        var good = Hex("AA55019201000C83");
        foreach (byte b in good[..4]) parser.ProcessReceivedByte(b);
        Program.Check(parsed == null && chained.Waiting, "fragmented ack waits");
        foreach (byte b in good[4..]) parser.ProcessReceivedByte(b);
        Program.Check(parsed != null && parsed.Length == 8, "full-frame parser delivers complete ack");
        Program.Check(chained.Receive(parsed!) && chained.OutcomeValue == EnterBootloaderSession.Outcome.Accepted, "chained ack accepted");

        var badCrc = (byte[])good.Clone(); badCrc[^1] ^= 1;
        var chained2 = new EnterBootloaderSession(_ => true, () => now);
        chained2.Request(1);
        var parser2 = new ProtocolHandler(); int delivered = 0;
        parser2.FrameReceived += _ => delivered++;
        foreach (byte b in badCrc) parser2.ProcessReceivedByte(b);
        Program.Check(delivered == 0 && chained2.Waiting, "bad CRC frame never reaches session");
        now += ProtocolConstants.ENTER_BL_ACK_TIMEOUT_MS; chained2.Tick();
        Program.Check(chained2.OutcomeValue == EnterBootloaderSession.Outcome.Timeout, "bad CRC path times out instead of accepting");

        /* ---- 回归: 功能码登记不与历史 IAP 旧值混淆 ---- */
        Program.Check(ProtocolConstants.FUNC_ENTER_BOOTLOADER == 0x12 && ProtocolConstants.RESP_ENTER_BOOTLOADER == 0x92,
            "enter BL function codes are 0x12/0x92");
        Program.Check(ProtocolConstants.FUNC_ENTER_IAP == 0x11 && ProtocolConstants.FUNC_READ_BMS_CACHE == 0x11,
            "legacy IAP stays on 0x11 (BMS cache in current firmware)");
        Program.Check(ProtocolConstants.GetFunctionCodeDescription(0x12).Contains("软件进入") &&
            ProtocolConstants.GetFunctionCodeDescription(0x92).Contains("软件进入"), "function code descriptions registered");
    }
}
