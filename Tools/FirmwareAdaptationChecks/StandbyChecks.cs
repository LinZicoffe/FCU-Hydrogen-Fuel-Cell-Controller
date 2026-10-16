using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

internal static class StandbyChecks
{
    public static void Run()
    {
        long now = 10;
        var sent = new List<byte[]>();
        StandbyPeriodSession session = null!;
        session = new StandbyPeriodSession(frame =>
        {
            Program.Check(!session.Blocks(frame), "own frame allowed during lease");
            sent.Add(frame); return true;
        }, () => now);
        byte[] Ack(byte address, byte result) => ProtocolHandler.BuildRequestFrame(address, 0x82, new[] { result });
        byte[] Readback(ushort seconds) => ProtocolHandler.BuildRequestFrame(1, 0x8A,
            new byte[] { 9, 2, 0, (byte)(seconds >> 8), (byte)seconds });
        Program.Check(session.Write(1, 10) && session.IsBusy, "standby write pending");
        Program.Check(session.Blocks(FirmwareMainProtocol.ReadBmsCache(1)), "cache blocked during standby lease");
        Program.Check(session.Blocks(ProtocolHandler.BuildRequestFrame(1, 2, new byte[18])), "other shared82 write blocked");
        Program.Check(!session.Blocks(ProtocolHandler.BuildRequestFrame(1, 9, Array.Empty<byte>())), "safety stop allowed");
        Program.Check(!session.Blocks(ProtocolHandler.BuildRequestFrame(1, 0x0D, new byte[] { 1 })), "return BMS allowed");
        Program.Check(!session.Read(1) && !session.Receive(Ack(2, 0)) && session.IsBusy, "busy/wrong address cannot finish write");
        Program.Check(session.Receive(Ack(1, 0)) && session.IsBusy && !session.Verified && sent[^1][3] == 0x0A, "success ACK requires readback");
        Program.Check(!session.Receive(Ack(1, 0)) && !session.Verified, "second82 cannot complete verify");
        Program.Check(session.Receive(Readback(10)) && session.Verified && !session.IsBusy && session.ActualPeriod == 10, "matching readback verifies saved period");
        session.Write(1, 10); session.Receive(Ack(1, 0)); session.Receive(Readback(60));
        Program.Check(!session.Verified && session.Status.Contains("不一致") && session.ActualPeriod == 60, "mismatch reported");
        foreach (byte result in new byte[] { 2, 3, 9 })
        {
            session.Write(1, 10); int before = sent.Count;
            session.Receive(Ack(1, result));
            Program.Check(!session.IsBusy && !session.Verified && sent.Count == before && session.Status.Contains("失败"), "negative ACK no blind retry");
        }
        session.Read(1);
        Program.Check(session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 9, 2, 0 })) &&
            !session.IsBusy && session.Status.Contains("不支持"), "old firmware empty0902 response");
        session.Read(1); now += 2999; session.Tick(); Program.Check(session.IsBusy, "before timeout");
        now++; session.Tick(); Program.Check(!session.IsBusy && session.Status.Contains("结果未知"), "timeout releases lease, result unknown");
        Program.Check(!session.Receive(Readback(10)), "idle ignores late readback");
        session.Write(1, 10); int epoch = session.Epoch;
        session.Reset();
        Program.Check(!session.IsBusy && session.Epoch != epoch && session.Status.Contains("中断"), "disconnect/mode/address resets transaction epoch");
        foreach (ushort seconds in new ushort[] { 1, 65535 })
        {
            session.Write(1, seconds); session.Receive(Ack(1, 0)); session.Receive(Readback(seconds));
            Program.Check(session.Verified && session.ActualPeriod == seconds, "standby edge write/read integration");
        }
        var failure = new StandbyPeriodSession(_ => false, () => now);
        Program.Check(!failure.Write(1, 10) && !failure.IsBusy && !failure.Verified, "failed send releases transaction");
    }
}
