using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

internal static class CalibrationChecks
{
    public static void Run()
    {
        byte[] H(string s) => Convert.FromHexString(s);
        var defaults = new CurrentCalibration(1, 0);
        var example = new CurrentCalibration(.945833f, -.788056f);
        byte[] Reply(CurrentCalibration c) => ProtocolHandler.BuildRequestFrame(1, 0x8A,
            new byte[] { 3, 2, 0 }.Concat(CurrentCalibrationProtocol.Write(1, c)[7..15]).ToArray());
        byte[] Ack(byte result, byte address = 1) => ProtocolHandler.BuildRequestFrame(address, 0x82, new[] { result });
        Program.Check(CurrentCalibrationProtocol.Write(1, defaults).SequenceEqual(H("AA5501050A03023F800000000000008C65")), "0302 documented default frame");
        Program.Check(CurrentCalibrationProtocol.Write(1, example).SequenceEqual(H("AA5501050A03023F72221DBF49BE0A623D")), "0302 documented negative-b frame");
        Program.Check(CurrentCalibrationProtocol.Read(1).SequenceEqual(H("AA55010A03030200127B")), "0302 documented read frame");
        byte[] back = H("AA55018A0B0302003F80000000000000E7B3");
        Program.Check(CurrentCalibrationProtocol.TryParse(back, 1, out var c) && c == defaults, "0302 documented readback");
        Program.Check(ResponseParser.TryValidateReadConfigResponse(back, 0x0302, out _), "0302 length registry");
        Program.Check(CurrentCalibrationProtocol.TryParse(Reply(example), 1, out c) && c == example, "0302 negative offset roundtrip");
        Program.Check(!CurrentCalibrationProtocol.TryParse(back, 2, out _), "0302 wrong address rejected");
        var bad = back.ToArray(); bad[^1] ^= 1;
        Program.Check(!CurrentCalibrationProtocol.TryParse(bad, 1, out _), "0302 corrupted CRC rejected");
        foreach (var data in new[] { new byte[] { 3, 2, 0 }, new byte[10], new byte[] { 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new byte[] { 3, 2, 1, 63, 128, 0, 0, 0, 0, 0, 0 } })
            Program.Check(!CurrentCalibrationProtocol.TryParse(ProtocolHandler.BuildRequestFrame(1, 0x8A, data), 1, out _), "0302 wrong length/id/subindex rejected");
        foreach (var invalid in new[] { new CurrentCalibration(0, 0), new CurrentCalibration(-1, 0), new CurrentCalibration(float.NaN, 0),
            new CurrentCalibration(float.PositiveInfinity, 0), new CurrentCalibration(1, float.NaN), new CurrentCalibration(1, float.NegativeInfinity) })
        {
            bool threw = false; try { CurrentCalibrationProtocol.Write(1, invalid); } catch (ArgumentOutOfRangeException) { threw = true; }
            Program.Check(threw, "0302 invalid float rejected before send");
        }
        foreach (var edge in new[] { new CurrentCalibration(float.Epsilon, float.MinValue), new CurrentCalibration(float.MaxValue, float.MaxValue) })
            Program.Check(CurrentCalibrationProtocol.TryParse(Reply(edge), 1, out c) && c == edge, "0302 finite float32 boundaries allowed");
        // 独立固定旧0301帧，不与0302参数体拼接。
        Program.Check(ProtocolHandler.BuildRequestFrame(1, 5, H("030100500014")).Length == 13 &&
            ResponseParser.TryValidateReadConfigResponse(ProtocolHandler.BuildRequestFrame(1, 0x8A, H("03010000500014")), 0x0301, out _), "0301 layout preserved");
        var parser = new ProtocolHandler(); int frames = 0;
        parser.FrameReceived += f => { if (CurrentCalibrationProtocol.TryParse(f, 1, out _)) frames++; };
        foreach (byte b in back[..8]) parser.ProcessReceivedByte(b);
        Program.Check(frames == 0, "0302 fragmented response waits");
        foreach (byte b in back[8..]) parser.ProcessReceivedByte(b);
        Program.Check(frames == 1, "0302 fragmented response completes");

        long now = 0; var sent = new List<byte[]>(); CurrentCalibrationSession session = null!;
        session = new CurrentCalibrationSession(f => { Program.Check(!session.Blocks(f), "0302 own lease frame allowed"); sent.Add(f); return true; }, () => now);
        Program.Check(session.Write(1, example) && session.IsBusy && !session.Read(1), "0302 one pending transaction");
        Program.Check(session.Blocks(CurrentCalibrationProtocol.Read(1)) && session.Blocks(FirmwareMainProtocol.WriteStandbyPeriod(1, 60)), "0302 blocks other shared writes/reads");
        Program.Check(!session.Blocks(ProtocolHandler.BuildRequestFrame(1, 9, Array.Empty<byte>())) &&
            !session.Blocks(ProtocolHandler.BuildRequestFrame(1, 0x0D, new byte[] { 1 })), "0302 safety stop and return allowed");
        Program.Check(!session.Receive(Ack(0, 2)) && session.IsBusy, "0302 wrong-address ACK ignored");
        Program.Check(session.Receive(Ack(0)) && session.IsBusy && !session.Verified && sent[^1].SequenceEqual(CurrentCalibrationProtocol.Read(1)), "0302 ACK initiates readback");
        Program.Check(session.Receive(Reply(example)) && session.Verified && session.Actual == example && !session.IsBusy, "0302 matching float32 verified");
        session.Write(1, example); session.Receive(Ack(0)); session.Receive(back);
        Program.Check(!session.Verified && session.Status.Contains("不一致") && session.Actual == defaults, "0302 mismatch not success");
        foreach (byte error in new byte[] { 2, 3, 5, 9 })
        {
            session.Write(1, example); int before = sent.Count; session.Receive(Ack(error));
            Program.Check(!session.IsBusy && !session.Verified && session.Status.Contains("失败") && sent.Count == before, "0302 error without blind retry");
        }
        session.Read(1); session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, H("030200")));
        Program.Check(session.Status.Contains("不支持") && session.Actual == null && !session.IsBusy, "0302 old firmware empty response");
        session.Read(1); var invalidData = H("0302007FC0000000000000");
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, invalidData));
        Program.Check(session.Status.Contains("非法") && session.Actual == null, "0302 nonfinite readback rejected");
        session.Read(1); now = 2999; session.Tick(); Program.Check(session.IsBusy, "0302 before timeout");
        now++; session.Tick(); Program.Check(!session.IsBusy && session.Status.Contains("结果未知"), "0302 timeout releases lease");
        Program.Check(!session.Receive(back), "0302 idle late frame ignored");
        session.Read(1); int epoch = session.Epoch; session.Reset();
        Program.Check(session.Epoch != epoch && !session.IsBusy && session.Actual == null && session.Status.Contains("中断"), "0302 reset invalidates session");
        session.Write(1, example); now += 3000; session.Tick();
        Program.Check(session.MayHaveLateAck && !session.Write(1, example) &&
            session.Blocks(FirmwareMainProtocol.WriteStandbyPeriod(1, 10)), "0302 write timeout guards all shared82 writes");
        Program.Check(session.Read(1) && session.Receive(back) && !session.Verified, "0302 diagnostic read remains available during quiet window");
        now += 3000;
        Program.Check(!session.MayHaveLateAck && session.Write(1, example), "0302 write recovers when late ACK window expires");
        session.Reset();
        var failure = new CurrentCalibrationSession(_ => false);
        Program.Check(!failure.Write(1, example) && !failure.IsBusy && !failure.Verified, "0302 write send failure");
        int sends = 0; var verifyFailure = new CurrentCalibrationSession(_ => ++sends == 1);
        verifyFailure.Write(1, example); verifyFailure.Receive(Ack(0));
        Program.Check(!verifyFailure.IsBusy && !verifyFailure.Verified && verifyFailure.Status.Contains("发送失败"), "0302 readback send failure");
    }
}
