using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FCU_氢燃料电池控;
using FCU_氢燃料电池控.Managers;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.ViewModels;

internal static class ControlSafetyChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Get(object o, string name) => o.GetType().GetField(name, Hidden)!.GetValue(o);
    private static void Set(object o, string name, object? value) => o.GetType().GetField(name, Hidden)!.SetValue(o, value);
    private static object? Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Hidden)!.Invoke(o, args);
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static byte[] Response(byte address, byte function, params byte[] data) => ProtocolHandler.BuildRequestFrame(address, function, data);

    private sealed class FakeSerial : SerialPortManager
    {
        public List<byte[]> Sent { get; } = new();
        public Action<byte[]>? OnSend;
        public bool FailSend;
        public override bool SendData(byte[] frame)
        {
            if (SendBlocked?.Invoke(frame) == true) return false;
            if (FailSend) return false;
            Sent.Add(frame);
            OnSend?.Invoke(frame);
            return true;
        }
    }

    private sealed class Harness : IDisposable
    {
        public MainWindow Window { get; } = new("控制安全离线检查");
        public MainViewModel Vm => (MainViewModel)Window.DataContext;
        public FakeSerial Serial { get; } = new();
        public Harness()
        {
            // 保留构造器装配的实际门禁，仅替换最底层串口传输。
            Serial.SendBlocked = Vm.GetSerialPortManager().SendBlocked;
            Set(Vm, "_serialPortManager", Serial);
            Serial.OnSend = frame => Call(Window, "OnFrameSent", frame);
            Set(Window, "_isSerialOpen", true);
            Vm.IsConnected = true;
            Vm.DeviceAddress = "1";
        }
        public void Receive(byte address, byte function, params byte[] data) => Call(Window, "OnFrameReceived", Response(address, function, data));
        public void Fan(bool enabled, byte duty) => Call(Window, "SendFanManualControl", enabled, duty);
        public string FanMode => ((TextBlock)Window.FindName("tbk_FanManualMode")).Text;
        public void Dispose()
        {
            Set(Window, "_isSerialOpen", false);
            Window.Close();
            Drain();
        }
    }

    public static void Run()
    {
        var failures = new List<string>();
        void Case(string name, Action<Harness> test)
        {
            try { using var h = new Harness(); test(h); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
        Case("状态错地址", h =>
        {
            h.Vm.StackState = "关机";
            h.Receive(2, 0xF1, 1, 1, 0, 2); Drain();
            Program.Check(h.Vm.StackState == "关机", "other-target F1 cannot overwrite current state");
            h.Receive(1, 0xF1, 1, 1, 0, 2); Drain();
            Program.Check(h.Vm.StackState == "运行", "current-target F1 still updates state");
        });
        Case("配方服务初始化", h =>
        {
            Program.Check(Get(h.Window, "_recipeService") is RecipeService &&
                ((ListBox)h.Window.FindName("lbx_RecipeList")).Items.Count > 0,
                "recipe page initializes its service and offers usable presets");
        });
        Case("排队状态切目标", h =>
        {
            h.Receive(1, 0xF1, 1, 1, 0, 2);
            h.Vm.DeviceAddress = "2"; Drain();
            Program.Check(h.Vm.StackState == "--", "queued old F1 cannot overwrite new target");
        });
        Case("排队状态断线", h =>
        {
            h.Receive(1, 0xF1, 1, 1, 0, 2);
            Call(h.Window, "OnSerialConnectionChanged", h.Serial, false); Drain();
            Program.Check(h.Vm.StackState == "--", "queued F1 cannot restore state after disconnect");
        });
        Case("报警错地址", h =>
        {
            h.Vm.StatusMessage = "当前目标状态";
            h.Receive(2, 0xF0, 0, 2, 4, 2, 88); Drain();
            Program.Check(h.Vm.StatusMessage == "当前目标状态", "other-target alarm does not change current status");
        });
        Case("启动应答错地址", h =>
        {
            h.Vm.StatusMessage = "等待当前目标";
            h.Receive(2, 0x88, 0); Drain();
            Program.Check(h.Vm.StatusMessage == "等待当前目标", "other-target start ACK is ignored");
        });
        Case("重复风扇请求", h =>
        {
            h.Fan(true, 30); h.Fan(false, 0);
            Program.Check(h.Serial.Sent.Count(f => f[3] == 0x0F) == 1, "one fan request may wait for ACK");
            h.Receive(1, 0x8F, 0); Drain();
            Program.Check(h.FanMode.Contains("30%") && !h.FanMode.Contains("自动温控"), "fan ACK confirms original manual duty");
        });
        Case("风扇应答错地址", h =>
        {
            h.Fan(true, 30); h.Receive(2, 0x8F, 0); Drain();
            Program.Check(Get(h.Window, "_pendingFanManualEnable") is true && h.FanMode.Contains("等待"), "wrong-target fan ACK cannot consume request");
            h.Receive(1, 0x8F, 0); Drain();
            Program.Check(h.FanMode.Contains("30%"), "correct fan ACK completes request");
        });
        Case("风扇排队切目标", h =>
        {
            h.Fan(true, 30); h.Receive(1, 0x8F, 0);
            h.Vm.DeviceAddress = "2"; string reset = h.FanMode; Drain();
            Program.Check(Get(h.Window, "_pendingFanManualEnable") == null && h.FanMode == reset && !h.FanMode.Contains("已确认"), "queued fan ACK is invalidated by target switch");
        });
        Case("重复排队应答不能消费新请求", h =>
        {
            h.Fan(true, 30);
            // 两个应答先入队；第一个执行后插入新请求，第二个旧回调必须失效。
            h.Receive(1, 0x8F, 0);
            Dispatcher.CurrentDispatcher.BeginInvoke(() => h.Fan(false, 0), DispatcherPriority.Normal);
            h.Receive(1, 0x8F, 0); Drain();
            Program.Check(Get(h.Window, "_pendingFanManualEnable") is false && h.FanMode.Contains("等待"),
                "queued duplicate fan ACK cannot confirm a newer request");
            h.Receive(1, 0x8F, 0); Drain();
            Program.Check(h.FanMode.Contains("自动温控"), "new request completes only with its own subsequent ACK");
        });
        Case("后台接收已进入旧请求后切换", h =>
        {
            h.Fan(true, 30);
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            int uiThread = Environment.CurrentManagedThreadId;
            var eventField = typeof(MainWindow).GetField("BlAppFrameReceived", Hidden)!;
            var oldHandler = (Action<byte[]>?)eventField.GetValue(h.Window);
            Action<byte[]> pauseRx = _ =>
            {
                if (Environment.CurrentManagedThreadId == uiThread) return;
                entered.Set();
                if (!resume.Wait(5000)) throw new TimeoutException("后台接收没有恢复");
            };
            eventField.SetValue(h.Window, oldHandler + pauseRx);
            Task rx = Task.Run(() => h.Receive(1, 0x8F, 0));
            try
            {
                Program.Check(entered.Wait(5000), "real background RX enters old request before UI switches");
                h.Receive(1, 0x8F, 0); Drain(); // 界面先完成A。
                h.Fan(false, 0); // B现在等待自己的应答。
                resume.Set(); rx.GetAwaiter().GetResult(); Drain();
                Program.Check(Get(h.Window, "_pendingFanManualEnable") is false && h.FanMode.Contains("等待"),
                    "old background ACK cannot recapture a newer fan request");
            }
            finally
            {
                resume.Set(); rx.GetAwaiter().GetResult();
                eventField.SetValue(h.Window, oldHandler);
            }
        });
        foreach (bool calibration in new[] { false, true })
        {
            Case(calibration ? "标定超时后的共享应答" : "待机超时后的共享应答", h =>
            {
                long now = 0;
                if (calibration)
                {
                    var session = new CurrentCalibrationSession(f => (bool)Call(h.Window, "SendFrame", f)!, () => now);
                    Set(h.Window, "_currentCalibration", session);
                    session.Write(1, new CurrentCalibration(1, 0)); now = 3000; session.Tick();
                }
                else
                {
                    var session = new StandbyPeriodSession(f => (bool)Call(h.Window, "SendFrame", f)!, () => now);
                    Set(h.Window, "_standbyPeriod", session);
                    session.Write(1, 10); now = 3000; session.Tick();
                }
                Set(h.Window, "_lastSharedSetAt", -3001L);
                bool load = (bool)Call(h.Window, "SendLoadControlCommand", (byte)0x15, new byte[] { 0, 50 })!;
                Program.Check(!load && h.Serial.Sent.Count == 1, "unknown dedicated write cannot donate82 to a new manual load");
                h.Receive(1, 0x82, 0); Drain();
                Program.Check(!h.Vm.StatusMessage.Contains("执行成功"), "late dedicated write82 remains unassociated");
            });
        }
        Case("后台状态接收时重连同地址", h =>
        {
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            int uiThread = Environment.CurrentManagedThreadId;
            var eventField = typeof(MainWindow).GetField("BlAppFrameReceived", Hidden)!;
            var oldHandler = (Action<byte[]>?)eventField.GetValue(h.Window);
            Action<byte[]> pauseRx = _ =>
            {
                if (Environment.CurrentManagedThreadId == uiThread) return;
                entered.Set();
                if (!resume.Wait(5000)) throw new TimeoutException("后台状态接收没有恢复");
            };
            eventField.SetValue(h.Window, oldHandler + pauseRx);
            Task rx = Task.Run(() => h.Receive(1, 0xF1, 1, 1, 0, 2));
            try
            {
                Program.Check(entered.Wait(5000), "background state frame enters old connection");
                Call(h.Window, "OnSerialConnectionChanged", h.Serial, false);
                Call(h.Window, "OnSerialConnectionChanged", h.Serial, true);
                resume.Set(); rx.GetAwaiter().GetResult(); Drain();
                Program.Check(h.Vm.StackState == "--", "old RX state cannot recapture epoch after same-address reconnect");
            }
            finally
            {
                resume.Set(); rx.GetAwaiter().GetResult();
                eventField.SetValue(h.Window, oldHandler);
            }
        });
        Case("手动发送失败", h =>
        {
            h.Serial.FailSend = true; h.Fan(true, 30);
            Program.Check(Get(h.Window, "_pendingFanManualEnable") == null && h.FanMode.Contains("失败"),
                "transport failure clears fan wait without claiming success");
            h.Serial.FailSend = false; h.Fan(false, 0);
            Program.Check(h.Serial.Sent.Count == 0, "uncertain failed send retains late ACK guard");
        });
        Case("风扇超时迟到", h =>
        {
            h.Fan(true, 30);
            Call(h.Window, "FanManualResponseTimeoutTimer_Tick", h.Window, EventArgs.Empty);
            h.Fan(false, 0); h.Receive(1, 0x8F, 0); Drain();
            Program.Check(h.Serial.Sent.Count(f => f[3] == 0x0F) == 1 && !h.FanMode.Contains("自动温控"), "timeout quiet window rejects replacement fan request and late ACK");
        });
        Case("手动控制独占", h =>
        {
            h.Fan(true, 30);
            Program.Check(!(bool)Call(h.Window, "SendFrame", Response(1, 0x07))!, "manual request blocks ordinary query");
            Program.Check((bool)Call(h.Window, "SendFrame", Response(1, 0x09))!, "safety stop remains available");
            Program.Check((bool)Call(h.Window, "SendFrame", Response(1, 0x0D, 1))!, "BMS polling return remains available");
        });
        Case("扩展时序与拉载互斥及回读", h =>
        {
            byte[] timing = Convert.FromHexString("020200030003000A0003000A000300320005000F");
            bool sent = (bool)Call(h.Window, "SetParameter", "扩展时序", (byte)3, (Func<byte[]>)(() => timing))!;
            bool load = (bool)Call(h.Window, "SendLoadControlCommand", (byte)0x15, new byte[] { 0, 50 })!;
            Program.Check(sent && !load && h.Serial.Sent.Count == 1, "0202 and manual load cannot share in-flight82");
            h.Receive(1, 0x82, 0); Drain();
            var write = (ParameterWriteSession)Get(h.Window, "_parameterWrite")!;
            Program.Check(write.IsBusy && !write.Verified && h.Serial.Sent[^1][3] == 0x0A, "0202 ACK triggers readback without claiming saved");
            h.Receive(1, 0x8A, new byte[] { 2, 2, 0 }.Concat(timing[2..]).ToArray()); Drain();
            Program.Check(write.Verified && !write.IsBusy, "0202 readback verifies all nine values");
        });
        Case("拉载应答错地址", h =>
        {
            Call(h.Window, "SendLoadControlCommand", (byte)0x15, new byte[] { 0, 50 });
            h.Receive(2, 0x82, 0); Drain();
            Program.Check(Get(h.Window, "_pendingLoadManualCmd") != null, "wrong-target82 cannot complete manual load");
            h.Receive(1, 0x82, 3); Drain();
            Program.Check(Get(h.Window, "_pendingLoadManualCmd") == null && h.Vm.StatusMessage.Contains("失败"),
                "correct-target load rejection is reported without success");
        });
        Case("拉载超时阻止其他共享写入", h =>
        {
            Call(h.Window, "SendLoadControlCommand", (byte)0x15, new byte[] { 0, 50 });
            Call(h.Window, "LoadManualResponseTimeoutTimer_Tick", h.Window, EventArgs.Empty);
            bool valve = (bool)Call(h.Window, "SetParameter", "扩展时序", (byte)3,
                (Func<byte[]>)(() => Convert.FromHexString("020200030003000A0003000A000300320005000F")))!;
            Program.Check(!valve && h.Serial.Sent.Count == 1, "late manual82 cannot be reused by a parameter write immediately after timeout");
            h.Receive(1, 0x82, 0); Drain();
            Program.Check(!h.Vm.StatusMessage.Contains("执行成功"), "idle late load ACK cannot produce a success message");
        });
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}
