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
        public override bool SendData(byte[] frame)
        {
            if (SendBlocked?.Invoke(frame) == true) return false;
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
            serial.SendBlocked = standby.Blocks;
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
            if (renderDirectory != null) RenderPages(window, renderDirectory);
        }
        finally
        {
            Set(window, "_isSerialOpen", false);
            window.Close();
            app.Shutdown();
        }
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
        Select("BMS电池管理"); Save(root, "BMS缓存-离线验收.png");
        Select("系统监控");
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
