using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Views
{
    /// <summary>
    /// 固件升级页 交互逻辑 (OpenBLT XCP over RS485 @57600)
    /// 依据: docs/实施步骤-OpenBLT升级与固件适配.md §4.3 状态机
    ///   协议实现 Protocol/OpenBltXcpClient.cs (逐命令对照 Boot-For-FCU 源码)
    ///   文件解析 Models/SrecFile.cs (地址窗 [0x08003000,0x0801F000) + 0x1D8 占位字强校验)
    /// 线程模型: 按钮事件(UI线程) → Task.Run 后台执行, UI 更新经 Dispatcher
    /// 创建日期: 2026-08-19 | 2026-09-21 重写为 OpenBLT 链路
    /// </summary>
    public partial class FirmwareUpgradeView : UserControl
    {
        /// <summary>宿主(MainWindow)提供的升级期桥接接口</summary>
        public interface IBlHostBridge
        {
            /// <summary>串口是否已打开</summary>
            bool IsSerialOpen { get; }
            /// <summary>当前串口名 (未打开返回 null)</summary>
            string? PortName { get; }
            /// <summary>当前配置的 FCU 设备地址 (AA55 帧用)</summary>
            byte DeviceAddress { get; }
            /// <summary>关闭并以新波特率重开串口 (UI线程执行, 含 100~300ms 重试), 返回是否成功</summary>
            bool ReopenSerial(int baudRate);
            /// <summary>发送 AA55 应用协议帧 (仅非升级模式可用, 走正常通道含日志)</summary>
            bool SendAppFrame(byte[] frame);
            /// <summary>AA55 完整帧到达事件 (前置检查 / 重启存活确认用)</summary>
            event Action<byte[]>? AppFrameReceived;
            /// <summary>进入/退出升级模式 (互斥窗口: 前置检查末进入, 重启确认存活后退出)</summary>
            void SetBlMode(bool on);
            /// <summary>
            /// 升级退出归一化: APP 存活 → 走正常断开链路(0x0D en=1 归还 BMS 总线);
            /// APP 不存活(BL常驻) → 直接关串口。UI线程执行。
            /// </summary>
            void NormalizeExit(bool appAlive);
        }

        /// <summary>BL XCP 波特率 (Boot-For-FCU blt_conf.h:98 @2a3904b, 单一来源见 ProtocolConstants.XCP_BL_BAUD_RATE)</summary>
        private const int BL_BAUD = ProtocolConstants.XCP_BL_BAUD_RATE;

        /// <summary>APP AA55 波特率 (固件 usart.c:42 = 9600, 复用 ProtocolConstants.DEFAULT_BAUD_RATE)</summary>
        private const int APP_BAUD = ProtocolConstants.DEFAULT_BAUD_RATE;

        private OpenBltXcpClient _client = null!;
        private IBlHostBridge? _bridge;
        private SrecFile? _srecFile;
        private CancellationTokenSource? _cts;
        private volatile bool _busy;

        /// <summary>升级流程进行中 (含全流程与退出归一化全程), 供 MainWindow.OnClosing 关窗拦截</summary>
        public bool IsBusy => _busy;
        private byte[]? _appFrameWait;    /* AppFrameReceived 事件暂存 (lock 保护) */
        private readonly object _frameLock = new();

        private BrushesStub _ui = null!;

        public FirmwareUpgradeView()
        {
            InitializeComponent();

            /* [步骤 3a 中间态] _cts/_busy 的赋值路径随 XCP 状态机在待办 D 恢复; 此处显式初始化消除 CS0649 */
            _cts = null;
            _busy = false;
        }

        /// <summary>由 MainWindow 在启动时注入串口管理器与宿主桥接</summary>
        public void Initialize(Managers.SerialPortManager serial, IBlHostBridge bridge)
        {
            _client = new OpenBltXcpClient(serial ?? throw new ArgumentNullException(nameof(serial)));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _bridge.AppFrameReceived += OnAppFrame;
            _ui = new BrushesStub(this);
        }

        /* ---------------- AA55 请求便捷构建 ---------------- */

        private byte[] BuildAppRequest(byte func)
            => ProtocolHandler.BuildRequestFrame(_bridge!.DeviceAddress, func, Array.Empty<byte>());

        private byte[] BuildAppRequest(byte func, byte[] data)
            => ProtocolHandler.BuildRequestFrame(_bridge!.DeviceAddress, func, data);

        /// <summary>升级模式下的串口字节入口 (由 MainWindow.SerialPort_DataReceived 分流调用)</summary>
        public void FeedSerialByte(byte b) => _client.FeedByte(b);

        /* ---------------- UI 辅助 ---------------- */

        private sealed class BrushesStub
        {
            private readonly FirmwareUpgradeView _v;
            private readonly Brush _ok, _pending, _active, _fail;
            public BrushesStub(FirmwareUpgradeView v)
            {
                _v = v;
                _ok = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                _pending = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
                _active = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                _fail = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                _ok.Freeze(); _pending.Freeze(); _active.Freeze(); _fail.Freeze();
            }
            private TextBlock Step(int n) => n switch
            {
                1 => _v.tbk_Step1, 2 => _v.tbk_Step2, 3 => _v.tbk_Step3, 4 => _v.tbk_Step4,
                _ => _v.tbk_Step5,
            };
            public void StepState(int n, string state)
            {
                /* 后台线程调用安全: 全部 UI 操作编组到 Dispatcher */
                _v.Dispatcher.BeginInvoke(() =>
                {
                    var tb = Step(n);
                    Brush b = state switch
                    {
                        "done" => _ok, "active" => _active, "fail" => _fail, _ => _pending,
                    };
                    tb.Foreground = b;
                    tb.FontWeight = state == "active" ? FontWeights.Bold : FontWeights.Normal;
                    if (state == "done") tb.Text = tb.Text + "  ✓";
                });
            }
            public void ResetSteps()
            {
                string[] texts =
                {
                    "① 选择固件文件并核对", "② 前置检查 (电堆关机 / BMS 总线接管)",
                    "③ 进入 Bootloader (加载指令; 无应答按提示断电重启)", "④ 连机 / 擦除 / 写入 / 固化", "⑤ 重启验证"
                };
                var tbs = new[] { _v.tbk_Step1, _v.tbk_Step2, _v.tbk_Step3, _v.tbk_Step4, _v.tbk_Step5 };
                for (int i = 0; i < 5; i++)
                {
                    tbs[i].Text = texts[i];
                    tbs[i].Foreground = _pending;
                    tbs[i].FontWeight = FontWeights.Normal;
                }
            }
        }

        private void Log(string msg)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                lbx_Log.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (lbx_Log.Items.Count > 500) lbx_Log.Items.RemoveAt(lbx_Log.Items.Count - 1);
            });
        }

        private void Status(string msg) => Dispatcher.BeginInvoke(() => tbk_Status.Text = msg);

        private void Progress(long done, long total)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                int pct = total > 0 ? (int)(done * 100 / total) : 0;
                pbar_Progress.Value = pct;
                tbk_Progress.Text = $"{pct}%  ({done / 1024.0:F1}/{total / 1024.0:F1} KB)";
            });
        }

        /* ---------------- AA55 应用帧等待 (前置检查/存活确认) ---------------- */

        private void OnAppFrame(byte[] frame)
        {
            lock (_frameLock)
            {
                _appFrameWait = frame;
                Monitor.Pulse(_frameLock);
            }
        }

        /// <summary>发送 AA55 请求并等待指定响应功能码 (超时返回 null)</summary>
        private byte[]? RequestAppFrame(byte[] request, byte respFunc, int timeoutMs, CancellationToken ct)
        {
            if (_bridge == null) return null;

            lock (_frameLock) _appFrameWait = null;

            for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
            {
                if (!_bridge.SendAppFrame(request)) return null;

                var deadline = Environment.TickCount64 + timeoutMs;
                lock (_frameLock)
                {
                    while (_appFrameWait == null)
                    {
                        long remain = deadline - Environment.TickCount64;
                        if (remain <= 0 || ct.IsCancellationRequested) break;
                        Monitor.Wait(_frameLock, (int)Math.Min(remain, 500));
                    }
                    if (_appFrameWait != null && _appFrameWait.Length > 3 && _appFrameWait[3] == respFunc)
                    {
                        return _appFrameWait;
                    }
                    _appFrameWait = null;
                }
            }
            return null;
        }

        /* ---------------- 按钮事件 ---------------- */

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                MessageBox.Show("升级进行中，不能更换文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 FCU 固件 (OpenBLT S-record)",
                Filter = "S-record 固件 (*.srec;*.s19;*.mot)|*.srec;*.s19;*.mot|所有文件 (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return;

            var srec = SrecFile.TryLoad(dlg.FileName, out string error);
            if (srec == null)
            {
                tbk_FilePath.Text = "未选择";
                tbk_PkgInfo.Text = $"包信息: 解析失败 - {error}";
                _srecFile = null;
                Log($"S-record 解析失败: {error}");
                return;
            }

            _srecFile = srec;
            tbk_FilePath.Text = dlg.FileName;
            tbk_PkgInfo.Text = $"固件: {srec.Length / 1024.0:F1} KB @ 0x{srec.BaseAddress:X8}" +
                               (srec.Warnings.Count > 0 ? $" | ⚠ {srec.Warnings.Count} 处间隙(0xFF 填充)" : string.Empty);
            Log($"S-record 加载成功: {Path.GetFileName(dlg.FileName)}, 镜像 {srec.Length}B @ 0x{srec.BaseAddress:X8}");
            foreach (var w in srec.Warnings) Log($"  ⚠ {w}");
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_bridge == null)
            {
                MessageBox.Show("页面未完成初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (_srecFile == null)
            {
                MessageBox.Show("请先选择有效的 S-record 固件文件 (.srec/.s19/.mot)", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!_bridge.IsSerialOpen)
            {
                MessageBox.Show("请先在 9600 波特率下正常连接 FCU (前置检查需要读取电堆状态)\n" +
                                "连接动作会自动完成 BMS 总线接管 (0x0D=0)", "提示",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            /* [步骤 3a 中间态] XCP 升级状态机在下一待办 (D) 接入 */
            MessageBox.Show("升级流程即将就绪：XCP 升级状态机正在接入中 (开发中间状态)。",
                "固件升级", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy) return;
            /* 纳秒级理论竞态防御: Cancel 与 finally 的 Dispose 交叠时 ObjectDisposedException */
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }
            Status("正在取消...");
            Log("用户请求取消");
        }

        private sealed class UpgradeException : Exception
        {
            public UpgradeException(string message) : base(message) { }
        }
    }
}
