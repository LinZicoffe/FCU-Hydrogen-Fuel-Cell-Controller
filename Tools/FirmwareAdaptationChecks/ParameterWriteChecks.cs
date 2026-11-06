using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

internal static class ParameterWriteChecks
{
    public static void Run()
    {
        QueryChecks();
        long now = 0;
        bool ready = false;
        bool sendOk = true;
        var sent = new List<byte[]>();
        var session = new ParameterWriteSession(f => { if (!sendOk) return false; sent.Add(f); return true; }, () => ready, () => now);
        byte[] seq = Convert.FromHexString("05010200320032003C0064"); // 两条：5A/0.5s，6A/1s
        var spec = ParameterWriteProtocol.Create(1, 4, seq, "时序");
        Program.Check(spec.ExpectedBody.SequenceEqual(Convert.FromHexString("00320032003C0064")) &&
            spec.ReadFrame[5..8].SequenceEqual(new byte[] { 5, 1, 0 }), "sequence readback omits write-only count");
        Program.Check(session.Write(1, 4, seq, "时序") && session.IsBusy && sent.Count == 0 && session.Status.Contains("尚未发送"),
            "in-flight queries drain before any write");
        Program.Check(!session.Write(1, 4, seq, "第二次") && sent.Count == 0, "duplicate click cannot overlap a write");
        Program.Check(session.Blocks(spec.ReadFrame) && !session.Blocks(ProtocolHandler.BuildRequestFrame(1, 9, Array.Empty<byte>())) &&
            !session.Blocks(ProtocolHandler.BuildRequestFrame(1, 13, new byte[] { 1 })), "lease blocks ordinary sends but preserves safety stop and BMS return");
        ready = true; session.Tick();
        Program.Check(sent.Count == 1 && sent[0][3] == 4 && !session.Verified, "write sent once before confirmation");
        seq[3] = 99;
        Program.Check(sent[0][8] == 0 && sent[0][9] == 50, "caller edits cannot mutate snapshot of written table");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 })) && sent.Count == 1,
            "another target ACK cannot advance session");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, Convert.FromHexString("05010000320032003C0064"))) && session.IsBusy,
            "readback before ACK cannot verify write");
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 }));
        Program.Check(sent.Count == 2 && sent[^1].SequenceEqual(spec.ReadFrame) && !session.Verified && session.IsBusy,
            "successful ACK only starts readback verification");
        var good = ProtocolHandler.BuildRequestFrame(1, 0x8A, Convert.FromHexString("05010000320032003C0064"));
        var bad = (byte[])good.Clone(); bad[^1] ^= 1;
        Program.Check(!session.Receive(bad) && session.IsBusy, "bad CRC cannot finish verification");
        session.Receive(good);
        Program.Check(session.Verified && !session.IsBusy && session.Status.Contains("已保存"), "success requires matching full table readback");
        seq = Convert.FromHexString("05010200320032003C0064");
        session.Write(1, 4, seq, "时序");
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 }));
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, Convert.FromHexString("05010000320032003D0064")));
        Program.Check(!session.Verified && !session.IsBusy && session.Status.Contains("不一致"), "default/old or different values cannot be reported saved");
        foreach (byte result in new byte[] { 2, 3, 9 })
        {
            int count = sent.Count;
            session.Write(1, 4, seq, "时序");
            session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new[] { result }));
            Program.Check(!session.IsBusy && !session.Verified && sent.Count == count + 1 && session.Status.Contains("失败"),
                "device rejection/save failure persists without blind retry or readback");
        }
        session.Write(1, 4, seq, "时序"); now += 3000; session.Tick();
        Program.Check(!session.IsBusy && !session.Verified && session.Status.Contains("超时") && session.MayHaveLateAck,
            "missing ACK produces unknown result and guards against late ACK reuse");
        Program.Check(session.Blocks(spec.WriteFrame) && !session.Blocks(spec.ReadFrame),
            "unknown outcome also blocks legacy shared-ACK writes but permits diagnostic reads");
        int epoch = session.Epoch;
        Program.Check(!session.Write(1, 4, seq, "时序") && session.Epoch == epoch, "unknown result cannot immediately contaminate next write");
        now += 3000;
        session.Write(1, 4, seq, "时序"); session.Reset();
        Program.Check(!session.IsBusy && !session.Verified && session.Epoch != epoch && !session.Receive(good), "reset cancels and rejects idle late readback");
        now += 3000;
        ready = false; session.Write(1, 4, seq, "时序"); int before = sent.Count; now += 5000; session.Tick();
        Program.Check(!session.IsBusy && sent.Count == before && session.Status.Contains("尚未发送"), "drain timeout never sends parameters");
        ready = true; sendOk = false;
        Program.Check(!session.Write(1, 4, seq, "时序") && !session.IsBusy && session.Status.Contains("发送失败") && !session.Verified,
            "false transport result is never labelled sent or saved");
        now += 3000; sendOk = true;
        session.Write(1, 4, seq, "时序"); sendOk = false;
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 }));
        Program.Check(!session.IsBusy && !session.Verified && session.Status.Contains("发送失败"), "readback send failure cannot claim saved");
        now += 3000; sendOk = true;
        session.Write(1, 4, seq, "时序"); session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 }));
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 5, 1, 0 }));
        Program.Check(!session.IsBusy && !session.Verified && session.Status.Contains("格式"), "unsupported/empty readback gives explicit error");
        byte[] flow = Convert.FromHexString("000A000A000A001E0000371E");
        var flowSpec = ParameterWriteProtocol.Create(1, 0x0E, flow, "流程");
        Program.Check(flowSpec.ParameterId == 0x0601 && flowSpec.ResponseFunction == 0x8E && flowSpec.ExpectedBody.SequenceEqual(flow),
            "flow write lacks ID and owns8E without borrowing shared82");
        session.Write(1, 0x0E, flow, "流程");
        Program.Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 })), "shared82 cannot acknowledge flow8E");
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8E, new byte[] { 0 }));
        session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 6, 1, 0 }.Concat(flow).ToArray()));
        Program.Check(session.Verified, "flow full-body readback verified");
        foreach (var entry in new[] { (2, "0101006400FA01F4138800140064138800FA"), (3, "0201000C000A009600030005"),
            (5, "030100500014"), (6, "0401140190025800C807080000"), (4, "0502017C03840000500005"),
            (4, "050303000F001E"), (11, "09010014") })
        {
            byte[] data = Convert.FromHexString(entry.Item2);
            var request = ParameterWriteProtocol.Create(1, (byte)entry.Item1, data, "参数");
            session.Write(1, (byte)entry.Item1, data, "参数");
            session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x82, new byte[] { 0 }));
            session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A,
                new byte[] { (byte)(request.ParameterId >> 8), (byte)request.ParameterId, 0 }.Concat(request.ExpectedBody).ToArray()));
            Program.Check(session.Verified, "ordinary parameter group layout verifies: " + entry.Item2[..4]);
        }
    }

    private static void QueryChecks()
    {
        long now = 0;
        var queries = new OrdinaryQueryTracker(() => now);
        var serialNumber = ProtocolHandler.BuildRequestFrame(1, 1, Array.Empty<byte>());
        queries.Sent(serialNumber);
        Program.Check(queries.Blocks(serialNumber) && queries.Receive(ProtocolHandler.BuildRequestFrame(1, 0x81, new byte[12])) && !queries.IsBusy,
            "UID read also drains before parameter writes");
        byte[] stack = ProtocolHandler.BuildRequestFrame(1, 7, Array.Empty<byte>());
        byte[] read = ProtocolHandler.BuildRequestFrame(1, 10, new byte[] { 5, 1, 0 });
        queries.Sent(stack); queries.Sent(read); now = 400;
        Program.Check(queries.IsBusy && queries.Blocks(stack) && queries.Blocks(read) && !queries.Expire(),
            "350ms silence cannot complete unanswered stack/config queries");
        queries.Receive(ProtocolHandler.BuildRequestFrame(2, 0x87, new byte[26]));
        Program.Check(queries.Blocks(stack), "another address cannot release pending stack query");
        queries.Receive(ProtocolHandler.BuildRequestFrame(1, 0x87, new byte[26]));
        Program.Check(!queries.Blocks(stack) && queries.IsBusy, "only matching stack request released");
        byte[] bad = ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 5, 1, 0, 0, 50, 0, 50 }); bad[^1] ^= 1;
        queries.Receive(bad);
        Program.Check(queries.Blocks(read), "bad config CRC does not drain query");
        Program.Check(queries.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 5, 1, 0, 0, 50, 0, 50 })) && !queries.IsBusy,
            "matching config response drains actual request");
        queries.Sent(read); now += 3000;
        Program.Check(queries.Expire() && !queries.IsBusy && queries.MayHaveLateReply, "timed-out queries retain late response quiet window");
        now += 3000;
        Program.Check(!queries.MayHaveLateReply && !queries.Receive(ProtocolHandler.BuildRequestFrame(1, 0x8A, new byte[] { 5, 1, 0 })),
            "unrequested late readback is not associated with UI input");
    }
}
