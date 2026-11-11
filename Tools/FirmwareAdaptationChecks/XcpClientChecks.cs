using FCU_氢燃料电池控.Protocol;

/// <summary>
/// OpenBltXcpClient 离线检查 (发送委托注入, 无串口依赖)。
/// 覆盖 [核查问题2修复] 的核心判据: 迟到的单字节成功应答无法冒充 SYNCH 屏障,
/// 以及应答按长度匹配/错误帧例外/发送失败标记等基础行为。
/// </summary>
internal static class XcpClientChecks
{
    public static void Run()
    {
        // 1. 发送失败 → Timeout 标记 (不抛异常)
        var failed = new OpenBltXcpClient(_ => false);
        var fr = failed.SetMta(0x08003000);
        Program.Check(!fr.Ok && fr.Timeout, "send failure flagged as timeout");

        // 2. 分片喂入 + 长度匹配应答被接受 (SetMta 期待 1B [FF])
        OpenBltXcpClient? echo = null;
        echo = new OpenBltXcpClient(f => { echo!.FeedByte(0x01); echo.FeedByte(0xFF); return true; });
        var m = echo.SetMta(0x08003000);
        Program.Check(m.Ok && m.Data.Length == 1 && m.Data[0] == 0xFF, "fragmented 1B positive accepted");

        // 3. 长度不符的非错误帧被丢弃 → 超时 (迟到帧不冒充, 核查问题2 根因的客户端面)
        OpenBltXcpClient? longOnly = null;
        longOnly = new OpenBltXcpClient(f =>
        {
            longOnly!.FeedByte(0x03);
            longOnly.FeedByte(0xFF);
            longOnly.FeedByte(0x11);
            longOnly.FeedByte(0x22);
            return true;
        });
        var lr = longOnly.SetMta(0x08003000);
        Program.Check(!lr.Ok && lr.Timeout, "wrong-length positive frame discarded, not mismatched onto command");

        // 4. 核心: 迟到的单字节成功应答无法通过 SYNCH 屏障 (expectedLen=2, 1B 被丢弃 → 超时)
        OpenBltXcpClient? staleAck = null;
        staleAck = new OpenBltXcpClient(f => { staleAck!.FeedByte(0x01); staleAck.FeedByte(0xFF); return true; });
        var synchStale = staleAck.Synch(80);
        Program.Check(synchStale.Timeout && !OpenBltXcpClient.IsSynchPositive(synchStale),
            "stale 1B FF cannot pass SYNCH barrier");

        // 5. 真 [FE 00]: 屏障通过, 但 Ok=false (不能用 Ok 判定)
        OpenBltXcpClient? synched = null;
        synched = new OpenBltXcpClient(f => { synched!.FeedByte(0x02); synched.FeedByte(0xFE); synched.FeedByte(0x00); return true; });
        var s2 = synched.Synch(80);
        Program.Check(!s2.Ok && OpenBltXcpClient.IsSynchPositive(s2), "FE00 is barrier-positive yet not Ok");

        // 6. 非零错误码 [FE xx] 不算屏障通过 (防旧命令错误应答冒充)
        OpenBltXcpClient? errored = null;
        errored = new OpenBltXcpClient(f => { errored!.FeedByte(0x02); errored.FeedByte(0xFE); errored.FeedByte(0x22); return true; });
        Program.Check(!OpenBltXcpClient.IsSynchPositive(errored.Synch(80)), "FE with nonzero error is not barrier-positive");

        // 7. 无设备 → SYNCH 超时 (allowNoResponse → Ok=true 但 Timeout=true, 屏障判 false)
        var none = new OpenBltXcpClient(_ => true);
        var s3 = none.Synch(50);
        Program.Check(s3.Ok && s3.Timeout && !OpenBltXcpClient.IsSynchPositive(s3), "no response is not barrier-positive");

        // 8. 错误帧不受 expectedLen 约束 (expectedLen=1 的 SetMta 收到 [FE 0x22] 显式错误)
        OpenBltXcpClient? explicitErr = null;
        explicitErr = new OpenBltXcpClient(f => { explicitErr!.FeedByte(0x02); explicitErr.FeedByte(0xFE); explicitErr.FeedByte(0x22); return true; });
        var er = explicitErr.SetMta(0x08003000);
        Program.Check(!er.Ok && !er.Timeout && er.ErrorCode == 0x22, "explicit error frame accepted regardless of expectedLen");

        // 9. UPLOAD 应答解析 ([FF + n 数据], expectedLen=n+1)
        byte[] payload = new byte[5];
        OpenBltXcpClient? uploader = null;
        uploader = new OpenBltXcpClient(f =>
        {
            uploader!.FeedByte(0x06);
            uploader.FeedByte(0xFF);
            for (int i = 0; i < 5; i++) uploader.FeedByte(payload[i]);
            return true;
        });
        var up = uploader.Upload(5);
        Program.Check(up.Ok && up.Data.Length == 6 && up.Data[0] == 0xFF, "upload response parsed");
        payload[2] = 0xAB;   // 数据变化应原样透出 (读回比对依赖)
        var up2 = uploader.Upload(5);
        Program.Check(up2.Ok && up2.Data[3] == 0xAB, "upload payload bytes verbatim");
    }
}
