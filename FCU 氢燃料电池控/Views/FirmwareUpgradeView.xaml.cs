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
        private bool? _lastExitAppAlive;    /* 最近一次退出归一化的探活结果 */

        /// <summary>读回校验开关 (实施文档 §7 联调第 2 项"UPLOAD 全镜像比对"入口, 联调时置 true, 平时关闭省时)。
        /// 用 static readonly 而非 const, 避免 const 常量传播触发 CS0162 不可达代码警告。</summary>
        private static readonly bool VERIFY_BY_UPLOAD = false;

        /// <summary>升级流程进行中 (含全流程与退出归一化全程), 供 MainWindow.OnClosing 关窗拦截</summary>
        public bool IsBusy => _busy;
        private byte[]? _appFrameWait;    /* AppFrameReceived 事件暂存 (lock 保护) */
        private readonly object _frameLock = new();

        private BrushesStub _ui = null!;

        public FirmwareUpgradeView()
        {
            InitializeComponent();
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

            var confirm = MessageBox.Show(
                $"即将开始固件升级 (OpenBLT XCP @ {BL_BAUD}, 镜像 {_srecFile.Length / 1024.0:F1} KB @ 0x{_srecFile.BaseAddress:X8})。\n\n" +
                "升级过程中: ① 电堆必须处于关机/故障状态 ② 设备将进入 Bootloader (自动加载指令; 不支持时需按提示断电重新上电) ③ 请勿关闭程序或拔出串口 ④ 总线上仅这一台 FCU\n\n确认开始?",
                "固件升级", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _busy = true;
            _ = Dispatcher.BeginInvoke(() =>
            {
                _ui.ResetSteps();
                btn_Start.IsEnabled = false;
                btn_Cancel.IsEnabled = true;
                pbar_Progress.Value = 0;
            });

            /* 不传第二个 ct: 若开跑前已取消, Task.Run 会直接转 Canceled 跳过方法体,
             * finally 不执行导致 _busy 卡 true (审查建议) */
            Task.Run(() => RunUpgradeAsync(ct));
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

        /* ---------------- 升级主流程 (OpenBLT XCP, 实施文档 §4.3 状态机) ---------------- */

        /// <summary>XCP 失败结果的统一文案</summary>
        private static string DescribeXcp(OpenBltXcpClient.XcpResult r)
            => r.Timeout ? "超时/串口发送失败"
             : r.Data.Length > 0 && r.Data[0] == ProtocolConstants.XCP_PID_ERR
                 ? $"XCP 错误 {ProtocolConstants.GetXcpErrorDescription(r.ErrorCode)} (0x{r.ErrorCode:X2})"
                 : "应答格式异常";

        private async Task RunUpgradeAsync(CancellationToken ct)
        {
            bool blModeEntered = false;   /* 是否已 SetBlMode(true): 决定退出归一化是否需要 6s 静默 */
            int activeStep = 5;           /* 当前进行中的步骤序号: 取消/失败时把 fail 标在真实步骤上 (终审 R1) */
            try
            {
                var img = _srecFile!.Image;
                uint baseAddr = _srecFile.BaseAddress;
                int total = img.Length;

                /* ---------- 步骤② 前置检查 (9600, AA55 模式; SetBlMode(true) 前完成一切 AA55 收发) ---------- */
                _ui.StepState(2, "active");
                activeStep = 2;
                Status("前置检查");
                Log("前置检查: 读电堆状态 (0x07)...");
                var readInfo = BuildAppRequest(0x07);
                byte[]? info = RequestAppFrame(readInfo, 0x87, 2000, ct);
                bool appOnline = info != null;
                if (info != null)
                {
                    if (info.Length < 6 || info[4] != 26)
                        throw new UpgradeException($"电堆状态应答长度异常 (LEN={info[4]})");
                    byte stackState = info[5];   /* 数据域首字节 = 工作状态 (0=OFF, 3=FAULT) */
                    if (stackState != 0 && stackState != 3)
                        throw new UpgradeException($"电堆未处于可升级状态 (状态={stackState}), 仅关机(0)/故障(3)态接受升级, 请先关闭电堆");
                    Log($"前置检查: 电堆状态 = {stackState} ({(stackState == 0 ? "关机" : "故障")}) ✓");
                }
                else
                {
                    Log("前置检查: 0x07 无应答 (设备可能 BL 常驻/未上电), 直接进入 XCP 轮询");
                }
                _ui.StepState(2, "done");

                /* ---------- 步骤③ 进入 Bootloader ---------- */
                _ui.StepState(3, "active");
                activeStep = 3;
                if (appOnline)
                {
                    /* 方案一: 加载指令 (帧布局待固件对齐 [待确认①], 无应答 1s 转断电路径)。
                     * 注意: 当前无应答约定, 固件适配落地后应改为等待确认帧再分支 (参照旧 0x11/0x91 先例),
                     *       避免加载已成功却仍提示断电、用户照做导致升级中途中断 */
                    var loadCmd = ProtocolHandler.BuildLoadBootloaderCommand(_bridge!.DeviceAddress);
                    Log($"发送加载指令: {BitConverter.ToString(loadCmd)}");
                    _ = _bridge.SendAppFrame(loadCmd);
                    await Task.Delay(1000, ct);
                    ct.ThrowIfCancellationRequested();
                    Log("加载指令无应答 (固件适配中), 转断电重启路径");

                    /* 方案二: 人工断电重启 (BL 复位后开放 500ms 后门窗口) */
                    Status("请断电重新上电 FCU (30 秒内)");
                    _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                        "请现在给 FCU 断电, 3 秒后重新上电。\n\n上电后引导程序短暂开放连接窗口, 上位机会自动连上 (30 秒内完成)。",
                        "进入 Bootloader", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                else
                {
                    Status("探测 Bootloader (常驻/断电重启均适用)");
                }

                /* ---------- 切换升级通道: SetBlMode(true) 后 SendAppFrame 直接被拒, AA55 通道到此为止 ---------- */
                _bridge!.SetBlMode(true);
                blModeEntered = true;
                _client.Reset();
                if (!_bridge.ReopenSerial(BL_BAUD))
                    throw new UpgradeException($"切换串口到 {BL_BAUD} 失败");
                Log($"串口已切换 {BL_BAUD}, XCP 轮询中...");

                /* ---------- XCP CONNECT 轮询: 单次等待 300ms + 间隔 50ms (实际周期 ≈350ms, <500ms 后门窗口) / 30s 总超时 ---------- */
                var connDeadline = Environment.TickCount64 + 30_000;
                OpenBltXcpClient.XcpResult? conn = null;
                while (Environment.TickCount64 < connDeadline && !ct.IsCancellationRequested)
                {
                    var r = _client.Connect(ProtocolConstants.XCP_CONNECT_WAIT_MS);
                    if (r.Ok && r.Data is { Length: 8 }) { conn = r; break; }
                    await Task.Delay(ProtocolConstants.XCP_CONNECT_POLL_INTERVAL_MS, ct);
                }
                if (conn == null)
                    throw ct.IsCancellationRequested
                        ? new OperationCanceledException(ct)
                        : new UpgradeException("30 秒内未连接到 OpenBLT 引导。请确认: ① 已按提示断电重新上电 ② 设备已 SWD 预烧 Boot-For-FCU 引导 ③ 总线上仅这一台 FCU");
                if (conn.Data[1] != 0x10 || conn.Data[3] != ProtocolConstants.XCP_MAX_CTO)
                    throw new UpgradeException($"CONNECT 应答异常: res=0x{conn.Data[1]:X2}, maxCto={conn.Data[3]} (期望 0x10/{ProtocolConstants.XCP_MAX_CTO})");
                Log($"XCP 已连接 (res=0x{conn.Data[1]:X2}, maxCto={conn.Data[3]}) ✓");
                _ = Dispatcher.BeginInvoke(() =>
                {
                    tbk_DeviceUid.Text = $"OpenBLT XCP @ {BL_BAUD}";
                    tbk_DeviceVer.Text = $"目标镜像 {total / 1024.0:F1} KB";
                });
                _ui.StepState(3, "done");

                /* ---------- 步骤④ 编程会话: START → SET_MTA → CLEAR → 块写入 → PROGRAM(0) ---------- */
                _ui.StepState(4, "active");
                activeStep = 4;
                Status("进入编程会话");
                var ps = _client.ProgramStart();
                if (!ps.Ok) throw new UpgradeException($"PROGRAM_START 失败: {DescribeXcp(ps)}");

                Status("擦除 APP 区 (按扇区整擦, 最长约 10s)");
                Log($"SET_MTA 0x{baseAddr:X8} + PROGRAM_CLEAR {total}B...");
                var mta = _client.SetMta(baseAddr);
                if (!mta.Ok) throw new UpgradeException($"SET_MTA 失败: {DescribeXcp(mta)}");
                var clear = _client.ProgramClear((uint)total);
                if (!clear.Ok) throw new UpgradeException($"PROGRAM_CLEAR 失败: {DescribeXcp(clear)}");
                Log("擦除完成");

                /* 写入: 整块 PROGRAM_MAX(128B 定长, MTA 自动后增), 尾块 PROGRAM(N); 单块失败重试 3 次(重试前 SET_MTA 重新同步) */
                Status("写入固件");
                int offset = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (offset < total)
                {
                    ct.ThrowIfCancellationRequested();
                    int len = Math.Min(ProtocolConstants.XCP_PROGRAM_MAX_DATA, total - offset);
                    var chunk = new byte[len];
                    Array.Copy(img, offset, chunk, 0, len);
                    uint addr = baseAddr + (uint)offset;

                    var w = len == ProtocolConstants.XCP_PROGRAM_MAX_DATA
                        ? _client.ProgramMax(chunk) : _client.Program(chunk);
                    for (int retry = 1; !w.Ok && retry <= 3; retry++)
                    {
                        Log($"写入块 @0x{addr:X8} 失败 ({DescribeXcp(w)}), 重试 {retry}/3 (重新 SET_MTA)...");
                        await Task.Delay(100, ct);
                        var m = _client.SetMta(addr);   /* MTA 状态未知, 重新定位 */
                        w = !m.Ok ? m
                          : len == ProtocolConstants.XCP_PROGRAM_MAX_DATA
                              ? _client.ProgramMax(chunk) : _client.Program(chunk);
                    }
                    if (!w.Ok)
                        throw new UpgradeException($"写入块 @0x{addr:X8} 失败 (重试 3 次): {DescribeXcp(w)}");

                    offset += len;
                    Progress(offset, total);
                    if ((offset & 0x7FF) == 0 || offset >= total)
                    {
                        double kbps = sw.Elapsed.TotalSeconds > 0 ? offset / 1024.0 / sw.Elapsed.TotalSeconds : 0;
                        Status($"写入 {offset * 100 / total}% ({kbps:F1} KB/s)");
                    }
                }
                Log($"写入完成: {total}B / {(total + ProtocolConstants.XCP_PROGRAM_MAX_DATA - 1) / ProtocolConstants.XCP_PROGRAM_MAX_DATA} 块, 耗时 {sw.Elapsed.TotalSeconds:F1}s");

                /* 固化: PROGRAM(0) → BL 触发 NvmDone 并计算写校验字 (flash.c FlashWriteChecksum) */
                Status("固化 (向量表校验字)");
                var fin = _client.Program(Array.Empty<byte>());
                if (!fin.Ok) throw new UpgradeException($"PROGRAM(0) 结束指令失败: {DescribeXcp(fin)}");
                Log("PROGRAM(0) 已确认, BL 已写入校验字 ✓");

                /* 读回校验 (联调开关, 默认关; 必须在 PROGRAM(0) 后——此前数据可能仍缓冲在 BL RAM bootblock) */
                if (VERIFY_BY_UPLOAD)
                    await VerifyByUploadAsync(img, baseAddr, ct);

                _ui.StepState(4, "done");

                /* ---------- 步骤⑤ 重启验证: PROGRAM_RESET 后 BL 直接启动新 APP (可能不回帧) ---------- */
                _ui.StepState(5, "active");
                activeStep = 5;
                Status("重启验证");
                _ = _client.ProgramReset();
                Log("PROGRAM_RESET 已发送 (引导直接启动新固件)");
                _client.Reset();
                await Task.Delay(2000, ct);

                _bridge.SetBlMode(false);
                if (!_bridge.ReopenSerial(APP_BAUD))
                    throw new UpgradeException("切换串口回 9600 失败, 请手动重开串口");

                bool alive = false;
                var aliveDeadline = Environment.TickCount64 + 10_000;
                var readSerial = BuildAppRequest(0x01);
                while (Environment.TickCount64 < aliveDeadline && !ct.IsCancellationRequested)
                {
                    var resp = RequestAppFrame(readSerial, 0x81, 1500, ct);
                    if (resp != null) { alive = true; break; }
                    await Task.Delay(500, ct);
                }
                if (!alive)
                    throw ct.IsCancellationRequested
                        ? new OperationCanceledException(ct)   /* 与 CONNECT 轮询一致: 区分取消与真超时 (审查建议) */
                        : new UpgradeException("设备重启后 10s 内无应答 (0x81), 存活确认失败");

                Log("新固件已启动 (0x81 序列号应答) ✓");
                _ui.StepState(5, "done");
                Status("升级成功");
                _bridge.NormalizeExit(true);
                _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                    $"固件升级成功!\n\n镜像: {total / 1024.0:F1} KB @ 0x{baseAddr:X8}\n" +
                    "串口已正常断开 (BMS 总线已归还 FCU), 重新打开串口即可继续使用。",
                    "固件升级", MessageBoxButton.OK, MessageBoxImage.Information));
            }
            catch (OperationCanceledException)
            {
                Log("升级已取消");
                Status("已取消");
                _ui.StepState(activeStep, "fail");
                await ExitNormalizationAsync(blModeEntered);
                if (_lastExitAppAlive == false)
                {
                    _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                        "升级已取消, 设备可能停留在 Bootloader (无有效固件)。\n\n" +
                        "处置: 重新执行一次完整升级即可修复 (BL 常驻时自动探测续升); 引导区与参数页不受影响。",
                        "固件升级", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
            catch (UpgradeException ex)
            {
                Log($"升级失败: {ex.Message}");
                Status("升级失败");
                await ExitNormalizationAsync(blModeEntered);
                string msg = ex.Message;
                if (_lastExitAppAlive == false)
                {
                    msg += "\n\n设备当前停留在 Bootloader (无有效固件), 串口已关闭。\n" +
                           "处置: 重新执行一次完整升级即可修复 (BL 常驻时自动探测续升)。\n" +
                           "注意: OpenBLT 引导区与参数页 (0x0801F000) 永不会被升级擦除; " +
                           "若后续 FCU 不轮询 BMS, 请在 BMS 页面使用\"归还 BMS 总线\"按钮自救。";
                }
                _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                    msg, "固件升级失败", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            catch (Exception ex)
            {
                Log($"升级异常: {ex}");
                Status("异常终止");
                await ExitNormalizationAsync(blModeEntered);
                _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                    $"升级过程发生未预期异常:\n\n{ex.Message}\n\n串口已按退出规则归一化, 请查看日志了解详情。",
                    "固件升级异常", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            finally
            {
                _busy = false;
                var oldCts = _cts;
                _cts = null;
                oldCts?.Dispose();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    btn_Start.IsEnabled = true;
                    btn_Cancel.IsEnabled = false;
                });
            }
        }

        /// <summary>UPLOAD 全镜像分段读回比对 (实施文档 §7 联调第 2 项入口, VERIFY_BY_UPLOAD 开关启用)。
        /// 必须在 PROGRAM(0) 之后调用 (此前尾块数据可能仍在 BL 的 RAM bootblock 未落盘);
        /// 比对跳过镜像偏移 0x1D8-0x1DB (校验字已被 BL 覆写, 与文件占位值不同属预期)。</summary>
        private async Task VerifyByUploadAsync(byte[] img, uint baseAddr, CancellationToken ct)
        {
            Status("读回校验 (UPLOAD)");
            Log("读回校验: SET_MTA + UPLOAD 分段比对...");
            var m = _client.SetMta(baseAddr);
            if (!m.Ok) throw new UpgradeException($"读回 SET_MTA 失败: {DescribeXcp(m)}");

            for (int off = 0; off < img.Length; off += ProtocolConstants.XCP_PROGRAM_MAX_DATA)
            {
                ct.ThrowIfCancellationRequested();
                int len = Math.Min(ProtocolConstants.XCP_PROGRAM_MAX_DATA, img.Length - off);
                var r = _client.Upload((byte)len);
                if (!r.Ok || r.Data.Length != len + 1)
                    throw new UpgradeException($"读回失败 @0x{baseAddr + (uint)off:X8}: {DescribeXcp(r)}");
                for (int i = 0; i < len; i++)
                {
                    if (off + i >= SrecFile.CHECKSUM_OFFSET && off + i < SrecFile.CHECKSUM_OFFSET + 4)
                        continue;   /* 校验字位置: BL 已写入二补码校验和, 与占位字不同属预期 */
                    if (r.Data[1 + i] != img[off + i])
                        throw new UpgradeException(
                            $"读回比对不一致 @0x{baseAddr + (uint)(off + i):X8}: 设备 0x{r.Data[1 + i]:X2} / 文件 0x{img[off + i]:X2}");
                }
                Progress(off + len, img.Length);
                await Task.Delay(1, ct);
            }
            Log("读回校验通过 ✓");
        }

        /// <summary>统一退出归一化: 停发 → (已进 BL 模式时)静默 6s → 切 9600 → 探活 → 正常断开(归还总线)。
        /// 未进入 BL 模式(前置检查即失败)时跳过静默等待, 避免白等并少打扰在跑的轮询 (审查建议)。
        /// 探活结果记录于 _lastExitAppAlive (true=APP存活已正常断开 / false=BL常驻已直接关串口 / null=未执行或重开失败),
        /// 供 catch 块生成准确的用户提示。</summary>
        private async Task ExitNormalizationAsync(bool blModeEntered)
        {
            _lastExitAppAlive = null;
            try
            {
                if (_bridge == null) return;

                if (blModeEntered)
                {
                    Log("退出流程: 静默 6s (让出总线)...");
                    await Task.Delay(6000);
                }
                else
                {
                    Log("退出流程: 未进入升级模式, 直接归一化");
                }

                _bridge.SetBlMode(false);
                _client.Reset();   /* 清 XCP 帧重组缓冲 */

                if (!_bridge.ReopenSerial(APP_BAUD))
                {
                    Log("警告: 切回 9600 失败, 请手动重开串口");
                    _bridge.NormalizeExit(false);
                    return;
                }

                /* 探活: 设备可能仍处于 BL 常驻 (半写入态) */
                var readSerial = BuildAppRequest(0x01);
                var resp = RequestAppFrame(readSerial, 0x81, 1500, CancellationToken.None);
                bool alive = resp != null;

                if (alive)
                {
                    Log("设备 APP 存活 - 执行正常断开 (0x0D en=1 归还 BMS 总线)");
                }
                else
                {
                    Log("设备无 APP 应答 (可能停留在 Bootloader 常驻): 直接关闭串口; 重新执行升级可修复");
                }
                _lastExitAppAlive = alive;
                _bridge.NormalizeExit(alive);
            }
            catch (Exception ex)
            {
                Log($"退出流程异常: {ex.Message}");
            }
        }

        private sealed class UpgradeException : Exception
        {
            public UpgradeException(string message) : base(message) { }
        }
    }
}
