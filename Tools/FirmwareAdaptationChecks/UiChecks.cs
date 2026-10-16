using System.IO.Ports;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FCU_氢燃料电池控;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Managers;
using FCU_氢燃料电池控.ViewModels;

internal static class UiChecks
{
    private sealed class FakeSerial : SerialPortManager
    {
        public readonly List<byte[]> Sent = new();
        public Action<byte[]>? OnSend;
        public override bool SendData(byte[] frame)
        {
            Sent.Add(frame);
            OnSend?.Invoke(frame);
            return true;
        }
    }

    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Get(object o, string name) => typeof(MainWindow).GetField(name, Flags)!.GetValue(o);
    private static void Set(object o, string name, object? value) => typeof(MainWindow).GetField(name, Flags)!.SetValue(o, value);
    private static object? Call(object o, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(o, args);

    public static void Run()
    {
        var app = new App(); app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow("离线回归");
        var vm = (MainViewModel)window.DataContext;
        var serial = new FakeSerial();
        typeof(MainViewModel).GetField("_serialPortManager", Flags)!.SetValue(vm, serial);
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
            Call(window, "OnSerialConnectionChanged", serial, false);
        }
        finally
        {
            Set(window, "_isSerialOpen", false);
            window.Close();
            app.Shutdown();
        }
    }
}
