using System.IO.Ports;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FCU_氢燃料电池控;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Managers;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.ViewModels;

internal static class UiChecks
{
    private sealed class FakeSerial : SerialPortManager
    {
        public readonly List<byte[]> Sent = new();
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

    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Get(object o, string name) => typeof(MainWindow).GetField(name, Flags)!.GetValue(o);
    private static void Set(object o, string name, object? value) => typeof(MainWindow).GetField(name, Flags)!.SetValue(o, value);
    private static object? Call(object o, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(o, args);

    public static void Run(string? renderDirectory = null)
    {
        var app = new App(); app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow("离线回归");
        var vm = (MainViewModel)window.DataContext;
        var serial = new FakeSerial();
        typeof(MainViewModel).GetField("_serialPortManager", Flags)!.SetValue(vm, serial);
        serial.ConnectionStateChanged += (_, connected) => Call(window, "OnSerialConnectionChanged", serial, connected);
        void Port(string port) => typeof(SerialPortManager).GetField("_serialPort", Flags)!.SetValue(serial, new SerialPort(port));
        var mode = (CheckBox)window.FindName("chk_DirectBmsMode");
        try
        {
            Program.Check(mode.IsChecked != true && serial.Sent.Count == 0, "default cache UI sends no takeover");
            mode.IsChecked = true; // 离线选择，不触发实际串口。
            Port("COM1"); Set(window, "_isSerialOpen", true);
            Program.Check((bool)Call(window, "SendBmsPollEnable", (byte)0)!, "lease acquired on COM1");
            Port("COM2");
            Program.Check(!(bool)Call(window, "SendBmsPollEnable", (byte)0)! && (string?)Get(window, "_bmsBusOwnerPort") == "COM1",
                "unreturned owner cannot be overwritten by another port");
            Port("COM1");
            serial.OnSend = f =>
            {
                if (f[3] == 0x0D && f[5] == 1)
                {
                    Set(window, "_bmsPollAckReceived", true); Set(window, "_bmsPollAckSuccess", true);
                }
            };
            // 地址非法时归还仍应使用先前owner地址1；故意直接改字段，避免先触发地址变更归还。
            var savedAddress = (string)typeof(MainViewModel).GetProperty("DeviceAddress")!.GetValue(vm)!;
            var addressField = typeof(MainViewModel).GetFields(Flags).First(f => f.Name == "_deviceAddress");
            addressField.SetValue(vm, "invalid");
            Call(window, "OnBeforeSerialClose");
            Program.Check(serial.Sent[^1][2] == 1 && serial.Sent[^1][5] == 1 && !(bool)Get(window, "_bmsBusMayBeOwned")!,
                "invalid address textbox still returns saved owner");
            addressField.SetValue(vm, savedAddress);
            // 接管等待DoEvents期间切换模式，嵌套归还的ACK不能被外层当作接管成功。
            serial.OnSend = f =>
            {
                if (f[3] != 0x0D) return;
                if (f[5] == 0) Dispatcher.CurrentDispatcher.BeginInvoke(() => mode.IsChecked = false);
                else { Set(window, "_bmsPollAckReceived", true); Set(window, "_bmsPollAckSuccess", true); }
            };
            Call(window, "TakeOverBmsBusWithConfirm");
            Program.Check(mode.IsChecked == false && !(bool)Get(window, "_bmsTakeoverConfirmed")! &&
                serial.Sent[^1][5] == 1 && !(bool)Get(window, "_bmsBusMayBeOwned")!, "mode switch cancels pumped takeover permanently");
            Set(window, "_isSerialOpen", false); mode.IsChecked = true; Set(window, "_isSerialOpen", true);
            serial.Sent.Clear();
            serial.OnSend = f =>
            {
                if (f[3] != 0x0D) return;
                if (f[5] == 0) Dispatcher.CurrentDispatcher.BeginInvoke(() => vm.DeviceAddress = "2");
                else { Set(window, "_bmsPollAckReceived", true); Set(window, "_bmsPollAckSuccess", true); }
            };
            Call(window, "TakeOverBmsBusWithConfirm");
            Program.Check(mode.IsChecked == false && !(bool)Get(window, "_bmsTakeoverConfirmed")! &&
                serial.Sent.All(f => f[2] == 1) && serial.Sent[^1][5] == 1,
                "address change during pump returns original target without new takeover");
            Set(window, "_isSerialOpen", false); mode.IsChecked = true; Set(window, "_isSerialOpen", true);
            serial.OnSend = f =>
            {
                if (f[3] != 0x0D) return;
                if (f[5] == 0) Dispatcher.CurrentDispatcher.BeginInvoke(() =>
                {
                    Call(window, "CancelBmsTakeover"); // 手动归还按钮在确认弹窗前调用的同一入口
                    Call(window, "OnBeforeSerialClose");
                });
                else { Set(window, "_bmsPollAckReceived", true); Set(window, "_bmsPollAckSuccess", true); }
            };
            Call(window, "TakeOverBmsBusWithConfirm");
            Program.Check(!(bool)Get(window, "_bmsTakeoverConfirmed")! && serial.Sent[^1][5] == 1,
                "manual return cancels outer takeover even though checkbox remains selected");
            mode.IsChecked = false;
            // 默认缓存连接/断开不会写0D。
            serial.Sent.Clear(); serial.OnSend = null;
            Call(window, "OnSerialConnectionChanged", serial, true);
            Call(window, "OnBeforeSerialClose");
            Program.Check(serial.Sent.All(f => f[3] != 0x0D), "cache connection and normal close do not write Flash");
            var standby = (StandbyPeriodSession)Get(window, "_standbyPeriod")!;
            var calibration = (CurrentCalibrationSession)Get(window, "_currentCalibration")!;
            var parameterWrite = (ParameterWriteSession)Get(window, "_parameterWrite")!;
            serial.SendBlocked = f => standby.Blocks(f) || calibration.Blocks(f) || parameterWrite.Blocks(f) ||
                ((OrdinaryQueryTracker)Get(window, "_ordinaryQueries")!).Blocks(f);
            var input = (TextBox)window.FindName("tbx_StandbyPeriod");
            var write = (Button)window.FindName("btn_WriteStandbyPeriod");
            foreach (string invalid in new[] { "0", "65536", "1.5", "-1", "abc" })
            {
                input.Text = invalid; int count = serial.Sent.Count;
                write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(serial.Sent.Count == count && !standby.IsBusy, "invalid period UI sends nothing");
                Call(window, "RenderStandbyPeriod");
                Program.Check(((TextBlock)window.FindName("tbk_StandbyPeriodStatus")).Text.Contains("整数"),
                    "period validation survives periodic rendering");
            }
            input.Text = "10"; write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(standby.IsBusy && serial.Sent[^1][3] == 0x0B, "standby button sends write");
            Program.Check(!(bool)Call(window, "SetParameter", "test", (byte)3, (Func<byte[]>)(() => new byte[12]))!,
                "shared SetParameter reports false when lease blocks it");
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 }));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Program.Check(standby.IsBusy && !standby.Verified && serial.Sent[^1][3] == 0x0A, "actual82 route asks readback before success");
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x8A, new byte[] { 9, 2, 0, 0, 10 }));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Program.Check(standby.Verified && input.Text == "10" && ((TextBlock)window.FindName("tbk_StandbyPeriodStatus")).Text.Contains("一致"),
                "actual8A route fills period and verifies");
            Call(window, "OnFrameSent", ProtocolHandler.BuildRequestFrame(2, 3, new byte[12]));
            int sentBeforeQuiet = serial.Sent.Count;
            Call(window, "BeginStandbyPeriod", false);
            Program.Check(!standby.IsBusy && serial.Sent.Count == sentBeforeQuiet && standby.Status.Contains("等待响应"),
                "real OnFrameSent shared82 quiet window gates standby start");
            Set(window, "_lastSharedSetAt", Environment.TickCount64 - 3000);
            Call(window, "BeginStandbyPeriod", true);
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 }));
            standby.Reset(); standby.Read(2);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Program.Check(standby.IsBusy && !standby.Verified && !standby.Status.Contains("保存成功"),
                "queued ACK before reset cannot advance a new epoch");
            var view = window.FindName("FirmwareUpgradeView");
            var bridge = (FCU_氢燃料电池控.Views.FirmwareUpgradeView.IBlHostBridge)
                view.GetType().GetField("_bridge", Flags)!.GetValue(view)!;
            Program.Check(!bridge.TryPrepareUpgrade(), "upgrade rejected while standby transaction pending");
            standby.Reset(); Set(window, "_lastSharedSetAt", -3001L);
            Set(window, "_pendingSocReadDeviceAddress", (byte?)2);
            Program.Check(!bridge.TryPrepareUpgrade(), "upgrade rejected while standalone SOC read pending");
            Set(window, "_pendingSocReadDeviceAddress", null);
            Program.Check(!bridge.TryPrepareUpgrade() && standby.MayHaveLateAck,
                "upgrade also waits for cancelled standby write late ACK window");
            typeof(StandbyPeriodSession).GetField("_quietUntil", Flags)!.SetValue(standby, Environment.TickCount64 - 1);
            Program.Check(bridge.TryPrepareUpgrade() && (bool)Get(window, "_upgradePrecheckMode")!, "upgrade preparation locks ordinary sends");
            Program.Check(!(bool)Call(window, "SendFrame", FirmwareMainProtocol.ReadBmsCache(2))!, "cache cannot send during upgrade prechecks");
            view.GetType().GetField("_busy", Flags)!.SetValue(view, true);
            bridge.SetBlMode(true); bridge.SetBlMode(false); // 模拟S8恢复AA55分流，但探活尚未结束
            Program.Check((bool)Get(window, "_upgradePrecheckMode")! && !(bool)Get(window, "_blUpgradeMode")! &&
                !(bool)Call(window, "SendFrame", FirmwareMainProtocol.ReadBmsCache(2))!, "S8 still blocks ordinary requests while AA55 probe runs");
            Program.Check(!((FrameworkElement)window.FindName("pnl_SerialActions")).IsEnabled, "upgrade locks target address and serial actions until completion");
            view.GetType().GetField("_busy", Flags)!.SetValue(view, false);
            bridge.SetBlMode(false);
            Program.Check(!(bool)Get(window, "_upgradePrecheckMode")!, "precheck exit always unlocks");
            Program.Check(!(bool)Get(window, "_suppressNextAutoBmsPoll")!, "final upgrade exit clears reconnect suppression");
            Call(window, "OnSerialConnectionChanged", serial, false);
            mode.IsChecked = true;
            serial.OnSend = f =>
            {
                if (f[3] == 0x0D) { Set(window, "_bmsPollAckReceived", true); Set(window, "_bmsPollAckSuccess", true); }
            };
            Call(window, "OnSerialConnectionChanged", serial, true);
            Program.Check((bool)Get(window, "_bmsTakeoverConfirmed")! && serial.Sent[^1][3] == 0x0D && serial.Sent[^1][5] == 0,
                "ordinary reconnect after upgrade still takes over explicit direct diagnostic mode");
            Call(window, "UpdateLoadBlockBanner", new FCU_氢燃料电池控.Models.BmsData { Online = true, Soc = 99 });
            Program.Check(((TextBlock)window.FindName("tbk_LoadBlockReason")).Text.Contains("门控与BMS故障耦合不可用"),
                "direct diagnostic data must not claim FCU gates remain active");
            Call(window, "OnBeforeSerialClose");
            Call(window, "OnSerialConnectionChanged", serial, false);
            // 覆盖真实NormalizeExit→VM.Close→BeforeSerialClose→归还→关闭闭环。
            foreach (string exitPath in new[] { "success", "cancel", "precheck-failure" })
            {
                Port("COM1"); Set(window, "_isSerialOpen", true);
                serial.OnSend = f =>
                {
                    if (f[3] == 0x0D)
                        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(f[2], 0x8D, new byte[] { 0 }));
                };
                Call(window, "SendBmsPollEnable", (byte)0);
                Set(window, "_lastSharedSetAt", -3001L);
                Program.Check(bridge.TryPrepareUpgrade(), $"{exitPath}: prepare with saved BMS owner");
                view.GetType().GetField("_busy", Flags)!.SetValue(view, true);
                if (exitPath != "precheck-failure") bridge.SetBlMode(true);
                bridge.SetBlMode(false);
                Program.Check(!(bool)Call(window, "SendFrame", FirmwareMainProtocol.ReadBmsCache(2))!, $"{exitPath}: ordinary lock remains");
                int beforeReturn = serial.Sent.Count;
                Program.Check(bridge.NormalizeExit(true), $"{exitPath}: real NormalizeExit confirms return");
                Program.Check(serial.Sent.Skip(beforeReturn).Any(f => f[2] == 2 && f[3] == 0x0D && f[5] == 1) &&
                    !(bool)Get(window, "_bmsBusMayBeOwned")! && serial.PortName == null && !(bool)Get(window, "_isSerialOpen")!,
                    $"{exitPath}: owner returned with ACK before close");
                view.GetType().GetField("_busy", Flags)!.SetValue(view, false); bridge.SetBlMode(false);
            }
            // 未获归还ACK必须保留未归还owner，并返回失败，不能声称已恢复。
            Port("COM1"); Set(window, "_isSerialOpen", true); serial.OnSend = null;
            Call(window, "SendBmsPollEnable", (byte)0);
            Set(window, "_lastSharedSetAt", -3001L); bridge.TryPrepareUpgrade();
            view.GetType().GetField("_busy", Flags)!.SetValue(view, true); bridge.SetBlMode(false);
            Program.Check(!bridge.NormalizeExit(true) && (bool)Get(window, "_bmsBusMayBeOwned")! && serial.PortName == null,
                "unconfirmed return is not reported as restored");
            view.GetType().GetField("_busy", Flags)!.SetValue(view, false); bridge.SetBlMode(false);
            Set(window, "_bmsBusMayBeOwned", false); // 清理本假传输未确认场景，避免影响可选呈现
            mode.IsChecked = false;
            var cache = (BmsCacheSession)Get(window, "_bmsCache")!;
            cache.Reset(); cache.Request(2);
            int oldCacheEpoch = cache.Epoch;
            byte[] cachedFrame = ProtocolHandler.BuildRequestFrame(2, 0x91,
                Convert.FromHexString("033CFB2E1194000000000000030000000000"));
            Call(window, "OnFrameReceived", cachedFrame); // 已到RX但尚未派发到UI
            cache.Reset(); cache.Request(2); // 同地址重连后新的请求
            Program.Check(cache.Epoch != oldCacheEpoch, "cache reset/request advances epoch");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(cache.Snapshot == null && cache.Waiting, "queued cache from previous session cannot satisfy new same-address request");
            Call(window, "OnFrameReceived", cachedFrame);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(cache.Snapshot?.Soc == 60 && !cache.Waiting, "current session queued cache is accepted");
            cache.Reset(); Set(window, "_isSerialOpen", true); Port("COM1"); serial.OnSend = null;
            CheckCalibrationCurrent(window, vm);
            var kInput = (TextBox)window.FindName("tbx_CurrentCalibrationK");
            var bInput = (TextBox)window.FindName("tbx_CurrentCalibrationB");
            var calRead = (Button)window.FindName("btn_ReadCurrentCalibration");
            var calWrite = (Button)window.FindName("btn_WriteCurrentCalibration");
            var calStatus = (TextBlock)window.FindName("tbk_CurrentCalibrationStatus");
            Program.Check(kInput.Text == "1" && bInput.Text == "0", "0302 UI defaults are neutral coefficients");
            foreach (var invalid in new[] { ("0", "0"), ("-1", "0"), ("NaN", "0"), ("Infinity", "0"),
                ("1e100", "0"), ("1", "NaN"), ("1", "-Infinity"), ("1", "abc"), ("1", "1,5") })
            {
                kInput.Text = invalid.Item1; bInput.Text = invalid.Item2; Set(window, "_lastSharedSetAt", -3001L);
                int before = serial.Sent.Count; calWrite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Call(window, "RenderCurrentCalibration");
                Program.Check(!calibration.IsBusy && serial.Sent.Count == before && calStatus.Text.Contains("有限"), "0302 UI invalid input persists and sends nothing");
            }
            kInput.Text = "0.945833"; bInput.Text = "-0.788056";
            Call(window, "OnFrameSent", ProtocolHandler.BuildRequestFrame(2, 5, new byte[6]));
            int beforeCal = serial.Sent.Count; calRead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(!calibration.IsBusy && serial.Sent.Count == beforeCal && calStatus.Text.Contains("等待响应"), "0302 quiet window gates real UI");
            Set(window, "_lastSharedSetAt", -3001L);
            calWrite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var expectedCal = new FCU_氢燃料电池控.Models.CurrentCalibration(.945833f, -.788056f);
            Program.Check(calibration.IsBusy && serial.Sent[^1].SequenceEqual(CurrentCalibrationProtocol.Write(2, expectedCal)) &&
                !kInput.IsEnabled && !calWrite.IsEnabled, "0302 actual UI sends only BE k/b and freezes editing");
            Program.Check(!(bool)Call(window, "SendFrame", FirmwareMainProtocol.ReadBmsCache(2))! &&
                !(bool)Call(window, "SetParameter", "DCDC", (byte)5, (Func<byte[]>)(() => Convert.FromHexString("030100500014")))!, "0302 lease blocks ordinary legacy sends");
            Call(window, "BeginStandbyPeriod", false);
            Program.Check(!standby.IsBusy && !bridge.TryPrepareUpgrade(), "0302 blocks standby and upgrade during transaction");
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 }));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(calibration.IsBusy && !calibration.Verified && serial.Sent[^1].SequenceEqual(CurrentCalibrationProtocol.Read(2)), "0302 actual ACK starts readback before success");
            byte[] calBack = ProtocolHandler.BuildRequestFrame(2, 0x8A, new byte[] { 3, 2, 0 }
                .Concat(CurrentCalibrationProtocol.Write(2, expectedCal)[7..15]).ToArray());
            Call(window, "OnFrameReceived", calBack);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(calibration.Verified && kInput.IsEnabled && calStatus.Text.Contains("回读一致") &&
                float.Parse(kInput.Text, System.Globalization.CultureInfo.InvariantCulture) == expectedCal.K &&
                float.Parse(bInput.Text, System.Globalization.CultureInfo.InvariantCulture) == expectedCal.B, "0302 actual readback roundtrips float32 through UI");
            Set(window, "_lastSharedSetAt", -3001L); calRead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string savedK = kInput.Text, savedB = bInput.Text;
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x8A, new byte[] { 3, 2, 0 }));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(calStatus.Text.Contains("不支持") && kInput.Text == savedK && bInput.Text == savedB, "0302 old firmware cannot zero inputs or claim success");
            calRead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Call(window, "OnFrameReceived", calBack); calibration.Reset(); calibration.Read(2);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(calibration.IsBusy && calibration.Actual == null, "0302 queued old readback rejected after new same-address epoch");
            Call(window, "OnSerialConnectionChanged", serial, false);
            Program.Check(!calibration.IsBusy && calibration.Actual == null && calStatus.Text.Contains("中断"), "0302 disconnect clears pending session and reports uncertainty");
            Set(window, "_isSerialOpen", true); Set(window, "_lastSharedSetAt", -3001L); calibration.Read(2);
            vm.DeviceAddress = "3";
            Program.Check(!calibration.IsBusy && calibration.Actual == null, "0302 target change resets session");
            vm.DeviceAddress = "2"; calibration.Read(2);
            Call(window, "ResetFirmwareMainSession");
            Program.Check(!calibration.IsBusy && kInput.IsEnabled, "0302 common mode/upgrade reset releases UI");
            // 默认显示回归，呈现时不把示例系数当已保存默认值。
            kInput.Text = "1"; bInput.Text = "0";
            CheckParameterWrites(window, vm, serial);
            CheckPressureFields(window, vm, serial);
            CheckPollingTimeoutHealth(window, vm, serial);
            CheckProbeRecovery(window, vm, serial);
            CheckMonitoring(window, vm);
            ControlSafetyChecks.Run();
            if (renderDirectory != null) RenderPages(window, renderDirectory);
        }
        finally
        {
            Set(window, "_isSerialOpen", false);
            window.Close();
            app.Shutdown();
        }
    }

    private static void CheckCalibrationCurrent(MainWindow window, MainViewModel vm)
    {
        var group = (FrameworkElement)window.FindName("grp_CurrentCalibration");
        DependencyObject? parent = group;
        while (parent != null && parent is not TabItem) parent = LogicalTreeHelper.GetParent(parent);
        Program.Check(parent is TabItem tab && tab.Header?.ToString() == "电流设置", "0302 calibration belongs to current settings tab");
        var tabs = (TabControl)window.FindName("MainTabControl");
        tabs.SelectedItem = parent;
        var display = (TextBlock)window.FindName("tbk_CalibrationInputCurrent");
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        void Report(ushort current)
        {
            byte[] data = new byte[26];
            data[9] = (byte)(current >> 8); data[10] = (byte)current;
            data[13] = 0; data[14] = 99; // 输出电流9.9A，用于排除显示错通道。
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, data));
            Pump();
        }
        vm.IsConnected = true;
        Pump();
        Program.Check(display.Text == "--", "0302 current unavailable before first report");
        Report(123);
        Program.Check(display.Text == vm.DCDCInputCurrent && display.Text == (12.3).ToString("F1") &&
            display.Text != vm.DCDCOutputCurrent, "0302 current uses real 0x87 input current scaled by 0.1A");
        Report(245);
        Program.Check(display.Text == (24.5).ToString("F1"), "0302 current follows subsequent reports on the same tab");
        Report(0);
        Program.Check(display.Text == (0.0).ToString("F1"), "0302 current shows a valid zero reading");
        Report(123);
        vm.IsConnected = false;
        Pump();
        Program.Check(display.Text == "--", "0302 current hides old readings when disconnected");
        vm.IsConnected = true;
        vm.DeviceAddress = "3";
        Pump();
        Program.Check(display.Text == "--" && vm.DCDCInputCurrent == "--", "0302 current cleared when target changes");
        vm.DeviceAddress = "2";
    }

    private static void CheckParameterWrites(MainWindow window, MainViewModel vm, FakeSerial serial)
    {
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        void Receive(byte function, byte[] data)
        {
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, function, data)); Pump();
        }
        var session = (ParameterWriteSession)Get(window, "_parameterWrite")!;
        var status = (TextBlock)window.FindName("tbk_ParameterWriteStatus");
        Set(window, "_isSerialOpen", true); vm.IsConnected = true; vm.DeviceAddress = "2";
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        Set(window, "_awaitingStackInfo", false); serial.OnSend = null;
        // 底层发送返回false：真实时序按钮不再声称已发送。
        for (int i = 1; i <= 15; i++)
        {
            ((TextBox)window.FindName($"tbx_Seq{i}_Current")).Text = i == 15 ? "" : "5";
            ((TextBox)window.FindName($"tbx_Seq{i}_Time")).Text = i == 15 ? "" : "0.50";
        }
        serial.FailSend = true; int before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(!session.IsBusy && !session.Verified && serial.Sent.Count == before && status.Text.Contains("发送失败"),
            "actual sequence button reports transport false without sent/success claim");
        serial.FailSend = false;
        // 用可控时钟的新会话避免真实等待3秒的迟到ACK保护窗口。
        long now = 10000;
        session = new ParameterWriteSession(f => (bool)Call(window, "SendFrame", f)!,
            () => (bool)Call(window, "CanSendParameterWrite")!, () => now);
        Set(window, "_parameterWrite", session);
        serial.SendBlocked = f => ((StandbyPeriodSession)Get(window, "_standbyPeriod")!).Blocks(f) ||
            ((CurrentCalibrationSession)Get(window, "_currentCalibration")!).Blocks(f) || session.Blocks(f) ||
            ((OrdinaryQueryTracker)Get(window, "_ordinaryQueries")!).Blocks(f);
        Set(window, "_awaitingStackInfo", true); before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(session.IsBusy && serial.Sent.Count == before && status.Text.Contains("尚未发送"),
            "actual button queues behind existing monitoring request");
        Call(window, "PollingTimer_Tick", window, EventArgs.Empty);
        Call(window, "RequestBmsCache");
        Program.Check(serial.Sent.Count == before && !(bool)Call(window, "SendFrame", ProtocolHandler.BuildRequestFrame(2, 10, new byte[] { 5, 1, 0 }))!,
            "ordinary write suppresses monitoring/cache/manual reads while draining");
        Receive(0x87, new byte[26]); session.Tick(); Call(window, "RenderParameterWrite");
        Program.Check(serial.Sent.Count == before + 1 && serial.Sent[^1][3] == 4 &&
            !((GroupBox)window.FindName("grp_LoadSequences")).IsEnabled,
            "drained telemetry allows a single write and freezes submitted input group");
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(serial.Sent.Count == before + 1, "repeat click cannot insert second write before ACK");
        Program.Check((bool)Call(window, "SendFrame", ProtocolHandler.BuildRequestFrame(2, 9, Array.Empty<byte>()))! &&
            (bool)Call(window, "SendFrame", ProtocolHandler.BuildRequestFrame(2, 13, new byte[] { 1 }))!,
            "real transport lease preserves stack stop and BMS return");
        Receive(0x82, new byte[] { 9 });
        string failed = status.Text;
        Receive(0x87, new byte[26]); Receive(0xF2, new byte[14]); Call(window, "RenderParameterWrite");
        Program.Check(!session.IsBusy && !session.Verified && failed.Contains("存储失败") && status.Text == failed &&
            ((GroupBox)window.FindName("grp_LoadSequences")).IsEnabled,
            "save failure persists through both monitoring streams and releases group");
        // 回读出厂默认值而非5A，不得覆盖用户输入或提示保存成功。
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Receive(0x82, new byte[] { 0 });
        Program.Check(session.IsBusy && !session.Verified && serial.Sent[^1][3] == 10, "actual ACK route reads table before success");
        byte[] defaults = new byte[3 + 14 * 4]; defaults[0] = 5; defaults[1] = 1;
        for (int i = 0; i < 14; i++) { defaults[3 + i * 4 + 1] = 10; defaults[3 + i * 4 + 3] = 50; }
        Receive(0x8A, defaults);
        Program.Check(!session.Verified && status.Text.Contains("不一致") && ((TextBox)window.FindName("tbx_Seq1_Current")).Text == "5",
            "default readback cannot masquerade as successful write or overwrite edits");
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs()); Receive(0x82, new byte[] { 0 });
        for (int i = 0; i < 14; i++) defaults[3 + i * 4 + 1] = 50;
        Receive(0x8A, defaults);
        Program.Check(session.Verified && status.Text.Contains("回读一致"), "second complete sequence write can verify successfully");
        // 压力MVVM/ParameterManager同样走事务，不依赖真实打开的串口对象。
        vm.HighPressureMin = "100"; vm.HighPressureRed = "250"; vm.HighPressureYellow = "500"; vm.HighPressureMax = "5000";
        vm.LowPressureMin = "20"; vm.LowPressureMax = "100"; vm.HighPressureRange = "5000"; vm.LowPressureRange = "250";
        vm.WritePressureParamsCommand.Execute(null);
        Program.Check(session.IsBusy && serial.Sent[^1][3] == 2, "pressure VM command uses common write session");
        Receive(0x82, new byte[] { 0 }); Receive(0x8A, Convert.FromHexString("010100006400FA01F4138800140064138800FA"));
        Program.Check(session.Verified && (bool)Get(window, "_pressureParamsLoadedFromDevice")!, "pressure verified readback satisfies initial-load gate");
        // 旧会话已入站但尚未执行的ACK不能推进新的写入。
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 }));
        session.Reset(); now += 3000;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs()); before = serial.Sent.Count; Pump();
        Program.Check(session.IsBusy && serial.Sent.Count == before && !session.Verified,
            "queued previous write ACK cannot request readback for new epoch");
        now += 3000; session.Tick(); Call(window, "RenderParameterWrite");
        Program.Check(!session.IsBusy && status.Text.Contains("超时"), "actual integration exposes missing ACK timeout");
        now += 3000;
        session.Reset();
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        serial.OnSend = f => Call(window, "OnFrameSent", f); // 与真实 SerialPortManager.FrameSent 的统一接线相同。
        Call(window, "SendFrame", ProtocolHandler.BuildRequestFrame(2, 7, Array.Empty<byte>()));
        before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(session.IsBusy && (bool)Get(window, "_awaitingStackInfo")! && serial.Sent.Count == before,
            "manual07 observed at real send event prevents immediate parameter write");
        Receive(0x87, new byte[26]); session.Tick();
        Receive(0x82, new byte[] { 2 });
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        Call(window, "SendReadConfigCommand", (byte)2, (byte)5, (byte)1, (byte)0);
        before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        System.Threading.Thread.Sleep(400); session.Tick();
        Program.Check(session.IsBusy && serial.Sent.Count == before, "unanswered0A longer than350ms still prevents write");
        Receive(0x8A, defaults); session.Tick();
        Program.Check(session.IsBusy && !session.Verified && serial.Sent.Count == before + 1 && serial.Sent[^1][3] == 4,
            "old config response must drain before write and cannot verify new parameters");
        Receive(0x82, new byte[] { 0 }); Receive(0x8A, defaults);
        Program.Check(session.Verified, "new post-ACK readback verifies after old query has drained");
        ((TextBox)window.FindName("tbx_Seq1_Current")).Text = "7";
        serial.OnSend = f =>
        {
            if (f[3] == 10 && !session.IsBusy)
                Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x8A, defaults));
            Call(window, "OnFrameSent", f);
        };
        Call(window, "SendReadConfigCommand", (byte)2, (byte)5, (byte)1, (byte)0); Pump();
        Program.Check(((TextBox)window.FindName("tbx_Seq1_Current")).Text == (5.0).ToString("0.0"),
            "RX arriving before FrameSent observer still matches query in Dispatcher callback");
        ((TextBox)window.FindName("tbx_Seq1_Current")).Text = "5";
        serial.OnSend = f => Call(window, "OnFrameSent", f);
        // RX先到并排队，UI事件尚未返回时又开始写入：旧回包仍须完成排空，不能覆盖编辑。
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        ((TextBox)window.FindName("tbx_Seq1_Current")).Text = "7";
        Call(window, "SendReadConfigCommand", (byte)2, (byte)5, (byte)1, (byte)0);
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x8A, defaults));
        Set(window, "_lastParameterReadAt", -3001L);
        before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(session.IsBusy && serial.Sent.Count == before, "queued RX still holds query until UI response processing");
        Pump();
        Program.Check(!((OrdinaryQueryTracker)Get(window, "_ordinaryQueries")!).IsBusy &&
            ((TextBox)window.FindName("tbx_Seq1_Current")).Text == "7" && !session.Verified,
            "RX queued before write drains matching query without overwriting edits or verifying new write");
        session.Tick();
        Program.Check(session.IsBusy && serial.Sent.Count == before + 1 && serial.Sent[^1][3] == 4,
            "queued matching config response releases draining write without false timeout");
        Receive(0x82, new byte[] { 2 });
        ((TextBox)window.FindName("tbx_Seq1_Current")).Text = "5";
        var queries = new OrdinaryQueryTracker(() => now);
        Set(window, "_ordinaryQueries", queries);
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        Call(window, "SendReadConfigCommand", (byte)2, (byte)5, (byte)1, (byte)0);
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs()); before = serial.Sent.Count;
        now += 3000; Call(window, "TickOrdinaryQueries"); session.Tick();
        Program.Check(!session.IsBusy && serial.Sent.Count == before && status.Text.Contains("尚未发送"),
            "query timeout cancels queued write instead of releasing it onto unknown response");
        defaults[4] = 10;
        Receive(0x8A, defaults);
        Program.Check(((TextBox)window.FindName("tbx_Seq1_Current")).Text == "5", "unmatched late config cannot overwrite edits");
        serial.OnSend = null;
        Call(window, "ResetFirmwareMainSession");
        Set(window, "_ordinaryQueries", new OrdinaryQueryTracker());
        Set(window, "_parameterWrite", new ParameterWriteSession(f => (bool)Call(window, "SendFrame", f)!, () => true));
        serial.SendBlocked = f => ((StandbyPeriodSession)Get(window, "_standbyPeriod")!).Blocks(f) ||
            ((CurrentCalibrationSession)Get(window, "_currentCalibration")!).Blocks(f) || ((ParameterWriteSession)Get(window, "_parameterWrite")!).Blocks(f) ||
            ((OrdinaryQueryTracker)Get(window, "_ordinaryQueries")!).Blocks(f);
    }

    private static void CheckPressureFields(MainWindow window, MainViewModel vm, FakeSerial serial)
    {
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var tabs = (TabControl)window.FindName("MainTabControl");
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => t.Header?.ToString() == "FCU参数配置");
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1280, 780)); root.Arrange(new Rect(0, 0, 1280, 780)); root.UpdateLayout();
        var red = (TextBox)window.FindName("tbx_HighPressureRed");
        var yellow = (TextBox)window.FindName("tbx_HighPressureYellow");
        var write = (Button)window.FindName("btn_WritePressureParams");
        var status = (TextBlock)window.FindName("tbk_ParameterWriteStatus");
        var session = (ParameterWriteSession)Get(window, "_parameterWrite")!;
        Set(window, "_isSerialOpen", true); vm.IsConnected = true;
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        Set(window, "_pressureParamsLoadedFromDevice", true);
        red.Text = "250"; yellow.Text = "500"; vm.HighPressureRange = "300";
        Pump();
        int before = serial.Sent.Count;
        write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Program.Check(red.Visibility == Visibility.Visible && yellow.Visibility == Visibility.Visible && vm.HighPressureRed == "250" && vm.HighPressureYellow == "500",
            "pressure red/yellow always visible and bound on parameter page");
        Program.Check(serial.Sent.Count == before && !session.IsBusy && status.Text.Contains("黄警值") && status.Text.Contains("量程"),
            "range below visible threshold rejects whole pressure write with persistent field error");
        red.Text = "100"; yellow.Text = "200"; vm.HighPressureMin = "50"; vm.HighPressureMax = "300"; Pump();
        write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Program.Check(session.IsBusy && !red.IsEnabled && !yellow.IsEnabled &&
            serial.Sent[^1][5..23].SequenceEqual(Convert.FromHexString("01010032006400C8012C00140064012C00FA")),
            "visible pressure inputs build full eight-field big-endian write and freeze during confirmation");
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 0 })); Pump();
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x8A,
            Convert.FromHexString("0101000032006400C8012C00140064012C00FA"))); Pump();
        Program.Check(session.Verified && red.IsEnabled && yellow.IsEnabled && status.Text.Contains("回读一致"),
            "actual pressure button verifies all visible thresholds and range after ACK");
        vm.HighPressureMin = "100"; red.Text = "250"; yellow.Text = "500"; vm.HighPressureMax = "5000"; vm.HighPressureRange = "5000";
        Call(window, "ResetFirmwareMainSession");
    }

    private static void CheckPollingTimeoutHealth(MainWindow window, MainViewModel vm, FakeSerial serial)
    {
        Set(window, "_isSerialOpen", true); vm.IsConnected = true; vm.DeviceAddress = "2";
        var polling = (DispatcherTimer)Get(window, "_pollingTimer")!;
        var savedInterval = polling.Interval;
        foreach (int interval in new[] { 1000, 5000 })
        {
            Call(window, "ResetFirmwareMainSession");
            Set(window, "_awaitingStackInfo", false); Set(window, "_diagDoneForConnection", false);
            long now = 10000;
            Set(window, "_ordinaryQueries", new OrdinaryQueryTracker(() => now));
            serial.OnSend = f => Call(window, "OnFrameSent", f);
            polling.Interval = TimeSpan.FromMilliseconds(interval);
            Call(window, "PollingTimer_Tick", window, EventArgs.Empty);
            for (int i = 0; i < 5; i++)
            {
                // 驱动250ms超时清理后，再驱动较慢的轮询tick；不等真实设备或真实秒数。
                now += 3000; Call(window, "TickOrdinaryQueries");
                Program.Check(!(bool)Get(window, "_awaitingStackInfo")!, $"{interval}ms poll releases expired serial wait");
                Call(window, "PollingTimer_Tick", window, EventArgs.Empty);
            }
            Program.Check((bool)Get(window, "_diagDoneForConnection")! && serial.Sent[^1][2] == 0,
                $"{interval}ms polling retains missing-response diagnosis across query expiry");
            Call(window, "StopStackInfoLinkDiagnosis"); Set(window, "_diagProbePending", false);
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, new byte[26]));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Check(!(bool)Get(window, "_stackInfoResponseMissing")! && (int)Get(window, "_stackInfoMissCount")! == 0,
                $"{interval}ms poll valid target response clears missing-response health");
            Call(window, "btn_StopPolling_Click", window, new RoutedEventArgs());
            Program.Check(!(bool)Get(window, "_stackInfoResponseMissing")!, "stop polling clears missing-response health");
        }
        Set(window, "_awaitingStackInfo", true); Set(window, "_stackInfoResponseMissing", true);
        Set(window, "_diagDoneForConnection", true); Set(window, "_diagProbePending", true);
        vm.DeviceAddress = "3";
        Program.Check(!(bool)Get(window, "_awaitingStackInfo")! && !(bool)Get(window, "_stackInfoResponseMissing")! &&
            !(bool)Get(window, "_diagDoneForConnection")! && !(bool)Get(window, "_diagProbePending")!,
            "target change clears serial wait and polling diagnosis lifecycle together");
        vm.DeviceAddress = "2";
        polling.Interval = savedInterval;
        Call(window, "ResetFirmwareMainSession");
        serial.OnSend = null;
        Set(window, "_ordinaryQueries", new OrdinaryQueryTracker());
    }

    private static void CheckProbeRecovery(MainWindow window, MainViewModel vm, FakeSerial serial)
    {
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Set(window, "_isSerialOpen", true); vm.IsConnected = true; vm.DeviceAddress = "2";
        Call(window, "ResetFirmwareMainSession");
        Set(window, "_lastSharedSetAt", -3001L); Set(window, "_lastParameterReadAt", -3001L);
        long now = 10000;
        var queries = new OrdinaryQueryTracker(() => now);
        Set(window, "_ordinaryQueries", queries);
        var session = new ParameterWriteSession(f => (bool)Call(window, "SendFrame", f)!,
            () => (bool)Call(window, "CanSendParameterWrite")!, () => now);
        Set(window, "_parameterWrite", session);
        serial.OnSend = f => Call(window, "OnFrameSent", f);
        Call(window, "StartStackInfoLinkDiagnosis", (byte)2);
        var timer = (DispatcherTimer)Get(window, "_diagProbeTimer")!;
        Program.Check(timer.Interval.TotalMilliseconds == 600 && (bool)Get(window, "_diagProbePending")! && queries.IsBusy,
            "diagnosis starts actual 600ms timer and tracks broadcast query");
        System.Threading.Thread.Sleep(650); Pump();
        Program.Check(!(bool)Get(window, "_diagProbePending")! && Get(window, "_diagProbeTimer") == null && queries.IsBusy,
            "actual probe timeout releases diagnosis gate but preserves unanswered query isolation");
        int before = serial.Sent.Count;
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(session.IsBusy && serial.Sent.Count == before, "post-probe write waits for old broadcast query");
        now += 3000; Call(window, "TickOrdinaryQueries");
        Program.Check(!session.IsBusy && serial.Sent.Count == before, "unanswered broadcast query cancels queued write without sending");
        now += 3000;
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, new byte[26])); Pump();
        Call(window, "btn_SetLoadSequenceParams_Click", window, new RoutedEventArgs());
        Program.Check(session.IsBusy && serial.Sent[^1][3] == 4, "recovered target can write after probe timeout and quiet window");
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x82, new byte[] { 2 })); Pump();
        Call(window, "StartStackInfoLinkDiagnosis", (byte)2);
        Set(window, "_stackInfoResponseMissing", true);
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(0, 0x87, new byte[26])); Pump();
        Program.Check((bool)Get(window, "_stackInfoResponseMissing")!, "broadcast answer never clears configured-target health");
        timer = (DispatcherTimer)Get(window, "_diagProbeTimer")!;
        timer.Interval = TimeSpan.FromMilliseconds(1); System.Threading.Thread.Sleep(30); Pump();
        Program.Check(vm.StatusMessage.Contains("疑似设备地址不匹配") && !(bool)Get(window, "_diagProbePending")!,
            "answered probe result survives terminal pending cleanup");
        Call(window, "StartStackInfoLinkDiagnosis", (byte)2);
        Call(window, "btn_StopPolling_Click", window, new RoutedEventArgs());
        Program.Check(!(bool)Get(window, "_diagProbePending")! && Get(window, "_diagProbeTimer") == null,
            "stop polling cancels diagnosis timer and gate");
        Call(window, "StartStackInfoLinkDiagnosis", (byte)2);
        Call(window, "btn_StartPolling_Click", window, new RoutedEventArgs());
        Program.Check(!(bool)Get(window, "_diagProbePending")! && Get(window, "_diagProbeTimer") == null,
            "restart polling cancels prior diagnosis timer and gate");
        Call(window, "btn_StopPolling_Click", window, new RoutedEventArgs());
        Call(window, "ResetFirmwareMainSession");
        serial.OnSend = null;
        Set(window, "_ordinaryQueries", new OrdinaryQueryTracker());
        Set(window, "_parameterWrite", new ParameterWriteSession(f => (bool)Call(window, "SendFrame", f)!, () => true));
    }

    private static void CheckMonitoring(MainWindow window, MainViewModel vm)
    {
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        void Receive(byte function, byte[] data, byte address = 2)
        {
            Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(address, function, data));
            Pump();
        }
        Set(window, "_isSerialOpen", true);
        vm.DeviceAddress = "2";
        Call(window, "ResetMonitoring");
        byte[] stack = new byte[26];
        stack[0] = 2; stack[1] = 0x0D; stack[2] = 0xA6; stack[4] = 69;
        stack[8] = 118; stack[10] = 19;
        Receive(0x87, stack);
        Program.Check(vm.HighPressure == "3494" && vm.LowPressure == "69" && vm.DCDCInputCurrent == (1.9).ToString("F1"),
            "valid telemetry retains firmware pressure and current scaling");
        Set(window, "_awaitingStackInfo", true);
        Receive(0x87, Array.Empty<byte>());
        Program.Check(vm.HighPressure == "3494" && vm.DCDCInputCurrent == (1.9).ToString("F1") &&
            (bool)Get(window, "_awaitingStackInfo")! && ((TextBlock)window.FindName("tbk_StackDataStatus")).Text.Contains("无效"),
            "malformed87 preserves last data and cannot acknowledge polling");
        byte[] damaged = ProtocolHandler.BuildRequestFrame(2, 0x87, stack); damaged[^1] ^= 1;
        Call(window, "OnFrameReceived", damaged); Pump();
        Program.Check(vm.HighPressure == "3494", "invalid87 CRC never updates readings");
        Receive(0x87, new byte[26], 3);
        Program.Check(vm.HighPressure == "3494", "another device cannot overwrite telemetry");
        Receive(0x87, new byte[26], 0);
        Program.Check(vm.HighPressure == "3494", "broadcast probe cannot overwrite configured target telemetry");
        Receive(0x87, new byte[26]);
        Program.Check(vm.HighPressure == "0" && vm.DCDCInputCurrent == (0.0).ToString("F1"), "real zero telemetry remains valid");
        byte[] load = new byte[14]; load[2] = 19; load[3] = 1; load[4] = 0x7C; load[8] = 233;
        Receive(0xF2, load);
        Receive(0xF2, Array.Empty<byte>());
        Program.Check(((TextBlock)window.FindName("tbk_LoadCurrentPower")).Text == (23.3).ToString("F1") &&
            ((TextBlock)window.FindName("tbk_LoadTargetCurrent")).Text == (38.0).ToString("F1") &&
            ((Label)window.FindName("lbl_LoadPeakCurrent")).Content.ToString()!.Contains("扫描峰值"),
            "load values stay unmodified on malformed F2 and peak has correct label");
        Set(window, "_lastStackInfoAt", Environment.TickCount64 - 20000);
        Call(window, "RenderMonitoringStatus");
        Program.Check(((TextBlock)window.FindName("tbk_StackDataStatus")).Text.Contains("过期") && vm.HighPressure == "0",
            "stale data explicitly labelled without inventing replacement values");
        var timer = (DispatcherTimer)Get(window, "_pollingTimer")!;
        timer.Interval = TimeSpan.FromMilliseconds(500);
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, stack));
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0xF2, load));
        System.Threading.Thread.Sleep(3100); // 模拟 UI 忙，已经收到的报文仍排在 Dispatcher 队列。
        Pump();
        Call(window, "RenderMonitoringStatus"); // 明确刷新年龄，不依赖250ms定时器与队列的先后顺序。
        Program.Check(((TextBlock)window.FindName("tbk_StackDataStatus")).Text.Contains("过期") &&
            ((TextBlock)window.FindName("tbk_LoadDataStatus")).Text.Contains("过期"),
            "Dispatcher backlog cannot turn old RX packets into fresh readings");
        vm.DeviceAddress = "0";
        Receive(0x87, stack, 0);
        Program.Check(vm.HighPressure == "3494", "configured broadcast target retains original monitoring support");
        vm.DeviceAddress = "2";
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, stack));
        Call(window, "ResetFirmwareMainSession"); Pump();
        Program.Check(vm.HighPressure == "--" && ((TextBlock)window.FindName("tbk_LoadCurrentPower")).Text == "--",
            "queued previous-session telemetry cannot refill cleared target");
        Set(window, "_isSerialOpen", false);
    }

    private static void RenderPages(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var tabs = (TabControl)window.FindName("MainTabControl");
        var root = (FrameworkElement)window.Content;
        Set(window, "_isSerialOpen", true);
        ((CheckBox)window.FindName("chk_DirectBmsMode")).IsChecked = false;
        var cache = (BmsCacheSession)Get(window, "_bmsCache")!;
        cache.Request(2);
        cache.Receive(ProtocolHandler.BuildRequestFrame(2, 0x91,
            Convert.FromHexString("033CFB2E1194000000000000030000000000")));
        Call(window, "RenderBmsCache");
        void Select(string header)
        {
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => t.Header?.ToString() == header);
            root.Measure(new Size(1280, 780)); root.Arrange(new Rect(0, 0, 1280, 780)); root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
        }
        void Save(FrameworkElement element, string name)
        {
            int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
        }
        void ScrollTo(FrameworkElement element, bool top = false)
        {
            DependencyObject? parent = element;
            while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
            if (parent is ScrollViewer viewer && viewer.Content is Visual content)
            {
                viewer.ScrollToVerticalOffset(top ? 0 : Math.Max(0, element.TransformToAncestor(content).Transform(new Point()).Y - 35));
                root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
            }
        }
        ((MainViewModel)window.DataContext).IsConnected = true;
        ((ParameterWriteSession)Get(window, "_parameterWrite")!).ReportRejection("电流时序回读不一致，未确认保存；请检查设备后重试");
        Call(window, "RenderParameterWrite");
        Select("FCU参数配置"); ScrollTo((FrameworkElement)window.FindName("grp_PressureParameters"), true);
        Save(root, "压力整组与写入结果-离线验收.png");
        Select("BMS电池管理"); Save(root, "BMS缓存-离线验收.png");
        Select("电流设置");
        ((CurrentCalibrationSession)Get(window, "_currentCalibration")!).Reset();
        Call(window, "RenderCurrentCalibration");
        ((MainViewModel)window.DataContext).IsConnected = true;
        byte[] stackSample = new byte[26];
        stackSample[0] = 2; stackSample[1] = 0x0D; stackSample[2] = 0xA6; stackSample[4] = 69;
        stackSample[5] = 1; stackSample[8] = 118; stackSample[10] = 57;
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0x87, stackSample));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var calibrationGroup = (FrameworkElement)window.FindName("grp_CurrentCalibration");
        DependencyObject? calibrationScroll = calibrationGroup;
        while (calibrationScroll != null && calibrationScroll is not ScrollViewer) calibrationScroll = VisualTreeHelper.GetParent(calibrationScroll);
        if (calibrationScroll is ScrollViewer calibrationViewer && calibrationViewer.Content is Visual calibrationContent)
        {
            calibrationViewer.ScrollToVerticalOffset(Math.Max(0, calibrationGroup.TransformToAncestor(calibrationContent).Transform(new Point(0, 0)).Y - 35));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
        }
        Save(root, "输入拉载标定0302-离线验收.png");
        Call(window, "OnFrameReceived", ProtocolHandler.BuildRequestFrame(2, 0xF2,
            Convert.FromHexString("060039017C007602A202A2000001")));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        ScrollTo((FrameworkElement)window.FindName("tbk_LoadState"));
        Save(root, "拉载峰值与数据状态-离线验收.png");
        Select("系统监控");
        ScrollTo((FrameworkElement)window.FindName("tbk_StackDataStatus"), true);
        Save(root, "系统监控数据状态-离线验收.png");
        var input = (FrameworkElement)window.FindName("tbx_StandbyPeriod");
        DependencyObject? scroll = input;
        while (scroll != null && scroll is not ScrollViewer) scroll = VisualTreeHelper.GetParent(scroll);
        if (scroll is ScrollViewer viewer)
        {
            viewer.ScrollToEnd(); root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
        }
        Save(root, "待机周期-离线验收.png");
        tabs.SelectedItem = Get(window, "_firmwareUpgradeTab");
        root.UpdateLayout(); Save(root, "升级边界-离线验收.png");
        Set(window, "_isSerialOpen", false);
    }
}
