using FCU_氢燃料电池控.Protocol;

internal static class Program
{
    private static int _passed;
    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ProtocolChecks();
            CacheSessionChecks();
            StandbyChecks.Run();
            AlarmChecks();
            SrecChecks.Run();
            int renderIndex = Array.IndexOf(args, "--render-dir");
            string? renderDirectory = renderIndex >= 0 && renderIndex + 1 < args.Length ? args[renderIndex + 1] : null;
            if (args.Contains("--ui")) UiChecks.Run(renderDirectory);
            Console.WriteLine($"PASS {_passed} checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));

    private static void AlarmChecks()
    {
        foreach (ushort reason in new ushort[] { 0, 1, 2, 3, 8 })
        {
            var frame = ProtocolHandler.BuildRequestFrame(1, 0xF0, new byte[] { 0, 14, 4, 0, (byte)reason });
            var alarm = ResponseParser.ParseAlarmInfo(frame);
            Check(alarm.AlarmType == 14 && alarm.GetAlarmTypeName().Contains("看门狗"), "watchdog alarm decoded");
            string description = alarm.GetAlarmValueDescription();
            Check(reason is 1 or 3 ? description.Contains("IWDG") : description.Contains("WWDG") || description.Contains("原因位"), "watchdog reset reason bits");
        }
        var invalid = new FCU_氢燃料电池控.Models.AlarmData { AlarmType = 13 };
        Check(invalid.GetAlarmValueDescription().Contains("超时") && invalid.GetAlarmTypeName().Contains("超时"), "alarmD includes complete sample timeout");
        Check(ProtocolConstants.GetFunctionCodeDescription(0x11).Contains("缓存"), "0x11 current name is cache");
        Check(typeof(ProtocolHandler).GetMethod("BuildLoadBootloaderCommand") == null, "undefined boot command removed");
        Check(FCU_氢燃料电池控.Models.SrecFile.APP_BASE == 0x08003000 &&
            FCU_氢燃料电池控.Models.SrecFile.APP_END_EXCLUSIVE == 0x0801F000, "OpenBLT image address guards retained");
    }

    private static void CacheSessionChecks()
    {
        long now = 100;
        var sent = new List<byte[]>();
        var session = new FCU_氢燃料电池控.Services.BmsCacheSession(f => { sent.Add(f); return true; }, () => now);
        Check(session.Request(1) && session.Waiting && sent.Count == 1, "cache send");
        Check(!session.Request(1) && sent.Count == 1, "one cache in flight");
        Check(!session.Receive(ProtocolHandler.BuildRequestFrame(1, 0x91, new byte[] { 0 })), "legacy IAP cannot finish cache wait");
        Check(!session.Receive(ProtocolHandler.BuildRequestFrame(2, 0x91, new byte[18])), "wrong address cannot finish cache wait");
        now += 1999; session.Tick(); Check(session.Waiting, "cache before timeout");
        now++; session.Tick(); Check(!session.Waiting && session.Snapshot == null && session.Status.Contains("超时"), "cache timeout");
        Check(!session.Receive(Hex("AA55019112033CFB2E1194123456789ABCDEF0000017703796")), "late response ignored");
        session.Request(1);
        session.Receive(Hex("AA55019112033CFB2E1194123456789ABCDEF0000017703796"));
        Check(session.Online && session.SocValid, "valid snapshot at exact boundary");
        now++; session.Tick(); Check(!session.Online && !session.SocValid, "host elapsed time ages snapshot without further responses");
        session.Reset(); Check(!session.Waiting && session.Snapshot == null, "reset disconnect/mode/upgrade state");
        var failed = new FCU_氢燃料电池控.Services.BmsCacheSession(_ => false, () => now);
        Check(!failed.Request(1) && !failed.Waiting && failed.Status.Contains("失败"), "send failure rolls back wait");
        Check(sent.All(f => f[3] == 0x11 && f[4] == 0), "cache never writes0D or Modbus");
    }

    private static void ProtocolChecks()
    {
        // 独立固定夹具来自固件协议示例，避免测试只重复实现。
        Check(FirmwareMainProtocol.WriteStandbyPeriod(1, 10).SequenceEqual(Hex("AA55010B040902000AA2E6")), "standby write fixed CRC");
        Check(FirmwareMainProtocol.ReadStandbyPeriod(1).SequenceEqual(Hex("AA55010A03090200D5BA")), "standby read fixed CRC");
        var readback = Hex("AA55018A05090200000A6B11");
        Check(FirmwareMainProtocol.TryParseStandbyPeriod(readback, 1, out ushort period) && period == 10, "standby readback");
        Check(ResponseParser.TryValidateReadConfigResponse(readback, 0x0902, out _), "standby registered");
        Check(!FirmwareMainProtocol.TryParseStandbyPeriod(readback, 2, out _), "wrong address rejected");
        var bad = (byte[])readback.Clone(); bad[^1] ^= 1;
        Check(!FirmwareMainProtocol.TryParseStandbyPeriod(bad, 1, out _), "bad CRC rejected");
        Check(!FirmwareMainProtocol.TryParseStandbyPeriod(readback[..^1], 1, out _), "short frame rejected");
        bool threw = false;
        try { FirmwareMainProtocol.WriteStandbyPeriod(1, 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "zero period rejected");
        foreach (ushort edge in new ushort[] { 1, 65535 })
        {
            var frame = FirmwareMainProtocol.WriteStandbyPeriod(1, edge);
            Check(frame[7] == (byte)(edge >> 8) && frame[8] == (byte)edge && frame[4] == 4, "period edge");
        }

        // flags=3, SOC=60, current=-1234, voltage=4500, distinct status words, age=6000.
        byte[] data = Hex("033CFB2E1194123456789ABCDEF000001770");
        // CRC另用Python binascii.crc_hqx独立验算后冻结。
        var cacheFrame = Hex("AA55019112033CFB2E1194123456789ABCDEF0000017703796");
        var cacheParser = new ProtocolHandler(); int cachesReceived = 0;
        cacheParser.FrameReceived += _ => cachesReceived++;
        foreach (byte b in cacheFrame[..12]) cacheParser.ProcessReceivedByte(b);
        Check(cachesReceived == 0, "partial 18B cache waits");
        foreach (byte b in cacheFrame[12..]) cacheParser.ProcessReceivedByte(b);
        Check(cachesReceived == 1, "fragmented 18B cache complete");
        Check(FirmwareMainProtocol.TryParseBmsCache(cacheFrame, 1, out var cache) && cache != null &&
            cache.CurrentRaw == -1234 && cache.VoltageRaw == 4500 && cache.Alarm == 0x1234 &&
            cache.Protect == 0x5678 && cache.Fault == 0x9ABC && cache.SystemStatus == 0xDEF0 && cache.AgeMs == 6000 &&
            cache.Online && cache.SocValid, "cache field offsets, sign, 6000ms boundary");
        data[^1]++; cacheFrame = ProtocolHandler.BuildRequestFrame(1, 0x91, data);
        Check(FirmwareMainProtocol.TryParseBmsCache(cacheFrame, 1, out cache) && !cache!.Online && !cache.SocValid, "6001ms stale");
        Array.Fill(data, (byte)255, 14, 4);
        Check(FirmwareMainProtocol.TryParseBmsCache(ProtocolHandler.BuildRequestFrame(1, 0x91, data), 1, out cache) && !cache!.Online, "never received sentinel");
        data[0] = 1; data[1] = 255; Array.Clear(data, 14, 4);
        Check(FirmwareMainProtocol.TryParseBmsCache(ProtocolHandler.BuildRequestFrame(1, 0x91, data), 1, out cache) && cache!.Online && !cache.SocValid, "SOC invalid independently");
        data[0] = 0;
        Check(FirmwareMainProtocol.TryParseBmsCache(ProtocolHandler.BuildRequestFrame(1, 0x91, data), 1, out cache) && !cache!.Online, "disabled/offline flag");
        Check(!FirmwareMainProtocol.TryParseBmsCache(ProtocolHandler.BuildRequestFrame(1, 0x91, new byte[] { 0 }), 1, out _), "legacy IAP 1B not cache");
        Check(!FirmwareMainProtocol.TryParseBmsCache(cacheFrame, 2, out _), "cache wrong device");
        cacheFrame[^1] ^= 1;
        Check(!FirmwareMainProtocol.TryParseBmsCache(cacheFrame, 1, out _), "cache CRC");
        var request = FirmwareMainProtocol.ReadBmsCache(1);
        Check(request.Length == 7 && request[3] == 0x11 && request[4] == 0, "cache request empty");

        var parser = new ProtocolHandler(); int received = 0;
        parser.FrameReceived += _ => received++;
        foreach (byte b in readback[..5]) parser.ProcessReceivedByte(b);
        Check(received == 0, "partial frame waits");
        foreach (byte b in readback[5..]) parser.ProcessReceivedByte(b);
        Check(received == 1, "fragmented frame complete");
        var valve = Hex("AA55018A1502020000030003000A0003000A000300320005000F2750");
        Check(ResponseParser.TryValidateReadConfigResponse(valve, 0x0202, out _) &&
            ResponseParser.ParseValveTimingParams(valve).StartupH2Delay == 15, "existing valve 0202 regression");
        var general = Hex("AA55018A0509010000140332");
        Check(ResponseParser.TryValidateReadConfigResponse(general, 0x0901, out _) &&
            ResponseParser.ParseGeneralParams(general).ScanPeriod == 20, "existing 0901 regression");
    }
}
