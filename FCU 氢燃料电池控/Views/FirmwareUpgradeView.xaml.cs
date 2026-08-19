using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Services;

namespace FCU_氢燃料电池控.Views
{
    /// <summary>
    /// 固件升级页 交互逻辑
    /// 升级流程 S0~S9 与超时/重试档位依据:
    ///   docs/03-设计方案/FCU-RS485-Bootloader在线升级设计方案.md §4.4
    /// 线程模型: 按钮事件(UI线程) → Task.Run 后台执行, UI 更新经 Dispatcher [设计 §4.1]
    /// 创建日期: 2026-08-19
    /// </summary>
    public partial class FirmwareUpgradeView : UserControl
    {
        /// <summary>宿主(MainWindow)提供的升级期桥接接口 [设计 §4.2]</summary>
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
            /// <summary>AA55 完整帧到达事件 (S1 前置检查 / S8 存活确认用)</summary>
            event Action<byte[]>? AppFrameReceived;
            /// <summary>进入/退出升级模式 (互斥窗口: S1 末进入, S8 确认存活后退出 [设计 §4.2.2])</summary>
            void SetBlMode(bool on);
            /// <summary>
            /// 升级退出归一化 [设计 §4.4 退出规则]: APP 存活 → 走正常断开链路(0x0D en=1 归还 BMS 总线);
            /// APP 不存活(BL常驻) → 直接关串口。UI线程执行。
            /// </summary>
            void NormalizeExit(bool appAlive);
        }

        private const int CHUNK_SIZE = 1024;
        private const int BL_BAUD = 115200;
        private const int APP_BAUD = 9600;

        private BlUpgradeClient _client = null!;
        private IBlHostBridge? _bridge;
        private BlUpgFile? _pkg;
        private CancellationTokenSource? _cts;
        private volatile bool _busy;
        private bool? _lastExitAppAlive;    /* 最近一次退出归一化的探活结果 [全量终审 P2] */

        /// <summary>升级流程进行中 (含 S1~S9 与退出归一化全程), 供 MainWindow.OnClosing 关窗拦截 [全量终审 P2]</summary>
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
            _client = new BlUpgradeClient(serial ?? throw new ArgumentNullException(nameof(serial)));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _bridge.AppFrameReceived += OnAppFrame;
            _ui = new BrushesStub(this);
        }

        /* ---------------- AA55 请求便捷构建 ---------------- */

        private byte[] BuildAppRequest(byte func)
            => ProtocolHandler.BuildRequestFrame(_bridge!.DeviceAddress, func, Array.Empty<byte>());

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
                /* 后台线程调用安全: 全部 UI 操作编组到 Dispatcher [批次2审查 P0] */
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
                    "① 选择升级包并核对", "② 前置检查 (电堆关机 / BMS 总线接管)",
                    "③ 按提示断电重新上电 FCU, 等待 Bootloader 应答", "④ 擦除 / 写入 / 校验", "⑤ 重启验证"
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

        /* ---------------- AA55 应用帧等待 (S1/S8) ---------------- */

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
                Title = "选择 FCU 固件升级包",
                Filter = "升级包 (*.upg)|*.upg|所有文件 (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return;

            var pkg = BlUpgFile.TryLoad(dlg.FileName, out string error);
            if (pkg == null)
            {
                tbk_PkgInfo.Text = $"包信息: 解析失败 - {error}";
                _pkg = null;
                return;
            }

            _pkg = pkg;
            tbk_FilePath.Text = dlg.FileName;
            tbk_PkgInfo.Text = $"包信息: 版本 0x{pkg.FwVersion:X8} | 固件 {pkg.FwSize / 1024.0:F1} KB | " +
                               (pkg.IsUidWildcard ? "UID: 不限设备(通配)" : $"UID: {BitConverter.ToString(pkg.TargetUid)}");
            Log($"升级包加载成功: {Path.GetFileName(dlg.FileName)}, 固件 {pkg.FwSize}B, CRC32=0x{pkg.PayloadCrc32:X8}");
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_bridge == null)
            {
                MessageBox.Show("页面未完成初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (_pkg == null)
            {
                MessageBox.Show("请先选择有效的 .upg 升级包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
                $"即将开始固件升级 (版本 0x{_pkg.FwVersion:X8}, {_pkg.FwSize / 1024.0:F1} KB)。\n\n" +
                "升级过程中: ① 电堆必须处于关机状态 ② 过程中会提示断电重新上电 ③ 请勿关闭程序或拔出串口\n\n确认开始?",
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

            Task.Run(() => RunUpgradeAsync(ct), ct);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy) return;
            _cts?.Cancel();
            Status("正在取消...");
            Log("用户请求取消");
        }

        /* ---------------- 升级主流程 S1~S9 [设计 §4.4] ---------------- */

        private async Task RunUpgradeAsync(CancellationToken ct)
        {
            try
            {
                /* ---------- S1 前置检查 (9600, APP 协议) ---------- */
                Status("S1 前置检查");
                Log("S1: 前置检查 (电堆关机 / 连接状态)");
                _ui.StepState(2, "active");

                bool blResident = false;
                var readInfo = BuildAppRequest(0x07);   /* 读电堆信息 */
                byte[]? info = RequestAppFrame(readInfo, 0x87, 2000, ct);
                if (info != null)
                {
                    if (info.Length < 6 || info[4] != 26)
                    {
                        throw new UpgradeException($"电堆状态应答长度异常 (LEN={info[4]})");
                    }
                    byte stackState = info[5];   /* 数据域首字节 = 工作状态 */
                    if (stackState != 0)
                    {
                        throw new UpgradeException($"电堆未处于关机状态 (状态={stackState}), 请先关闭电堆再升级");
                    }
                    Log($"S1: 电堆状态 = {stackState} (关机) ✓");
                }
                else
                {
                    /* [全量终审 P0-1] 自救回退: 上次升级在 EraseApp 后失败时设备 BL 常驻
                     * (仅 115200/BL 协议应答), 0x07 永无应答 → 必须允许从这里直接续升,
                     * 否则"重新执行升级即可修复"的承诺被 S1 硬门槛堵死 (485 级变砖)。 */
                    Log("S1: 0x07 无应答, 探测 Bootloader 常驻模式 (115200)...");
                    _bridge!.SetBlMode(true);
                    if (!_bridge.ReopenSerial(BL_BAUD))
                    {
                        throw new UpgradeException("0x07 无应答且切换 115200 失败, 请检查串口");
                    }
                    _client.Reset();

                    var probe = _client.Request(BlProtocolHelper.Commands.GetUid, null, 500, expectedLen: 12);
                    if (!(probe.Ok && probe.Data is { Length: 12 }))
                    {
                        await Task.Delay(300, ct);
                        probe = _client.Request(BlProtocolHelper.Commands.GetUid, null, 500, expectedLen: 12);
                    }

                    if (probe.Ok && probe.Data is { Length: 12 })
                    {
                        blResident = true;
                        Log("S1: 设备处于 Bootloader 常驻 (上次升级未完成), 跳过电堆检查直接续升");
                    }
                    else
                    {
                        _bridge.SetBlMode(false);
                        _bridge.ReopenSerial(APP_BAUD);    /* 尽力恢复, 失败由用户手动处理 */
                        throw new UpgradeException(
                            "设备无应答: 既非运行中的应用 (0x07), 也非 Bootloader (115200 GetUID)。\n" +
                            "请检查供电/接线/设备地址后重试");
                    }
                }

                if (!blResident)
                {
                    /* 进入升级模式互斥窗口 [设计 §4.2.2: S1 末启用] */
                    _bridge!.SetBlMode(true);
                    _client.Reset();

                    if (!_bridge.ReopenSerial(BL_BAUD))
                    {
                        throw new UpgradeException($"切换串口到 {BL_BAUD} 失败");
                    }
                }
                Log($"S1: 串口已切换 {BL_BAUD}, 进入升级模式");

                /* ---------- S2 等待设备 (提示断电重启) ---------- */
                _ui.StepState(2, "done");
                _ui.StepState(3, "active");
                Status("S2 等待设备 (请断电重新上电)");
                Log("S2: 请现在给 FCU 断电, 3 秒后重新上电 (30 秒内有效)");

                byte[]? uid = null;
                var s2Deadline = Environment.TickCount64 + 30_000;
                /* 短超时(300ms)快探测 + 100ms 间隔 ≈ 400ms 周期, 保证 BL 上电 1s 同步窗口内 ≥2 次命中
                 * [批次2审查 P1: 原 2s 超时使周期 2.2s, 窗口命中率仅 ~45%] */
                while (Environment.TickCount64 < s2Deadline && !ct.IsCancellationRequested)
                {
                    var r = _client.Request(BlProtocolHelper.Commands.GetUid, null, 300, expectedLen: 12);
                    if (r.Ok && r.Data is { Length: 12 })
                    {
                        uid = r.Data;
                        break;
                    }
                    await Task.Delay(100, ct);
                }
                if (uid == null)
                {
                    throw ct.IsCancellationRequested
                        ? new OperationCanceledException(ct)
                        : new UpgradeException("30 秒内未收到 Bootloader 应答 (请确认已按提示断电重新上电)");
                }
                string uidStr = BitConverter.ToString(uid);
                _ = Dispatcher.BeginInvoke(() => tbk_DeviceUid.Text = uidStr);
                Log($"S2: Bootloader 应答, UID = {uidStr}");
                _ui.StepState(3, "done");

                /* ---------- S3 校验 ---------- */
                Status("S3 UID/版本校验");
                if (!_pkg!.IsUidMatch(uid))
                {
                    throw new UpgradeException($"设备 UID 不匹配: 包目标 {BitConverter.ToString(_pkg.TargetUid)} / 设备 {uidStr}");
                }
                Log("S3: UID 匹配通过");

                var ver = _client.GetVersion();
                if (ver.Ok && ver.Data is { Length: 8 })
                {
                    uint fwVer = BitConverter.ToUInt32(ver.Data, 0);
                    uint blVer = BitConverter.ToUInt32(ver.Data, 4);
                    _ = Dispatcher.BeginInvoke(() => tbk_DeviceVer.Text = $"0x{fwVer:X8} (BL 0x{blVer:X8})");
                    if (fwVer != 0xFFFFFFFF && fwVer > _pkg.FwVersion)
                    {
                        Log($"S3: 注意 - 设备当前版本 0x{fwVer:X8} 高于升级包 0x{_pkg.FwVersion:X8} (降级, 继续)");
                    }
                }
                else
                {
                    Log("S3: 读取版本失败 (不影响升级, 继续)");
                }

                /* ---------- S4 擦除 ---------- */
                _ui.StepState(4, "active");
                Status("S4 擦除 APP 区");
                Log("S4: EraseApp (0.6~1.2s)...");
                var erase = _client.EraseApp();
                if (!erase.Ok)
                {
                    throw new UpgradeException($"EraseApp 失败 (Status=0x{erase.Status:X2})");
                }
                Log("S4: 擦除完成");

                /* ---------- S5 写入 ---------- */
                Status("S5 写入固件");
                var payload = _pkg.Payload;
                int totalChunks = (payload.Length + CHUNK_SIZE - 1) / CHUNK_SIZE;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < totalChunks; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    int len = Math.Min(CHUNK_SIZE, payload.Length - i * CHUNK_SIZE);
                    var chunk = new byte[len];
                    Array.Copy(payload, i * CHUNK_SIZE, chunk, 0, len);

                    uint addr = BlProtocolHelper.APP_BASE + (uint)(i * CHUNK_SIZE);
                    var w = _client.WriteChunk(addr, chunk);
                    if (!w.Ok)
                    {
                        throw new UpgradeException(
                            w.Status == BlProtocolHelper.Status.Fail
                                ? $"写入块 {i + 1}/{totalChunks} 被拒绝 (Status=0x01, 目标非空), 须重新开始升级"
                                : $"写入块 {i + 1}/{totalChunks} 超时/校验失败 (重试 3 次后放弃)");
                    }

                    long done = (long)(i + 1) * CHUNK_SIZE;
                    Progress(Math.Min(done, payload.Length), payload.Length);
                    if ((i & 0x0F) == 0)
                    {
                        double kbps = sw.ElapsedMilliseconds > 0
                            ? Math.Min(done, payload.Length) / 1024.0 / sw.Elapsed.TotalSeconds : 0;
                        Status($"S5 写入 {Math.Min(done, payload.Length) * 100 / payload.Length}% ({kbps:F1} KB/s)");
                    }
                }
                Log($"S5: 写入完成, {payload.Length}B / {totalChunks} 块, 耗时 {sw.Elapsed.TotalSeconds:F1}s");

                /* ---------- S6 校验 ---------- */
                Status("S6 CRC32 校验");
                Log("S6: Verify...");
                var verify = _client.Verify((uint)payload.Length);
                if (!verify.Ok && verify.Data == null)
                {
                    throw new UpgradeException("Verify 无应答/帧损坏 (超时), 请重新执行升级");
                }
                uint deviceCrc = (verify.Data is { Length: 4 })
                    ? BitConverter.ToUInt32(verify.Data, 0) : 0u;
                if (deviceCrc != _pkg.PayloadCrc32)
                {
                    throw new UpgradeException($"Verify CRC32 不匹配: 设备 0x{deviceCrc:X8} / 本地 0x{_pkg.PayloadCrc32:X8}, 请重新执行升级");
                }
                Log($"S6: CRC32 校验通过 (0x{deviceCrc:X8})");

                /* ---------- S7 固化 + 复位 ---------- */
                Status("S7 写 META 并复位");
                var meta = _client.WriteMeta(_pkg.BuildWriteMetaData());
                if (!meta.Ok)
                {
                    throw new UpgradeException($"WriteMeta 失败 (Status=0x{meta.Status:X2})");
                }
                Log("S7: META 固化完成, 发送 Reset");
                _ui.StepState(4, "done");
                _ui.StepState(5, "active");
                _ = _client.ResetDevice();   /* 0x05 设备复位 (应答可能因复位发不全, 忽略结果) */
                await Task.Delay(300, ct);   /* 等设备复位完成 */
                _client.Reset();             /* 清接收状态机, 准备静默期 */

                /* ---------- S8 静默 + 重启验证 ---------- */
                Status("S8 重启验证");
                Log("S8: 静默等待设备重启 (2s)...");
                await Task.Delay(2000, ct);

                /* 退出升级模式互斥窗口 [设计 §4.2.2: S8 末解除] */
                _bridge!.SetBlMode(false);

                if (!_bridge.ReopenSerial(APP_BAUD))
                {
                    throw new UpgradeException("切换串口回 9600 失败, 请手动重开串口");
                }

                bool alive = false;
                var aliveDeadline = Environment.TickCount64 + 10_000;
                var readSerial = BuildAppRequest(0x01);
                while (Environment.TickCount64 < aliveDeadline && !ct.IsCancellationRequested)
                {
                    var resp = RequestAppFrame(readSerial, 0x81, 1500, ct);
                    if (resp != null) { alive = true; break; }
                    await Task.Delay(500, ct);
                }

                /* ---------- S9 完成 / 统一退出 ---------- */
                if (alive)
                {
                    Log("S8: 新固件已启动 (0x81 序列号应答) ✓");
                    _ui.StepState(5, "done");
                    Status("升级成功");
                    Log("S9: 升级成功 - 执行正常断开 (归还 BMS 总线)");
                    _bridge.NormalizeExit(true);
                    _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                        $"固件升级成功!\n\n新版本: 0x{_pkg.FwVersion:X8} ({_pkg.FwSize / 1024.0:F1} KB)\n" +
                        "串口已正常断开 (BMS 总线已归还 FCU), 重新打开串口即可继续使用。",
                        "固件升级", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                else
                {
                    throw new UpgradeException("设备 Reset 后 10s 内无应答 (0x81), 存活确认失败");
                }
            }
            catch (OperationCanceledException)
            {
                Log("升级已取消");
                Status("已取消");
                _ui.StepState(5, "fail");
                await ExitNormalizationAsync();
                if (_lastExitAppAlive == false)
                {
                    _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                        "升级已取消, 设备停留在 Bootloader (无有效固件)。\n\n" +
                        "处置: 重新执行一次完整升级即可修复 (会跳过电堆检查直接续升)。", "固件升级",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
            catch (UpgradeException ex)
            {
                Log($"升级失败: {ex.Message}");
                Status("升级失败");
                await ExitNormalizationAsync();
                /* 失败弹窗延迟到退出归一化之后(可附加设备真实状态), 编组 UI 线程
                 * [全量终审 P2: 消除双重弹窗 + 修正 S8 失败但设备实际存活时的误导文案] */
                string msg = ex.Message;
                if (_lastExitAppAlive == false)
                {
                    msg += "\n\n设备当前停留在 Bootloader (无有效固件), 串口已关闭。\n" +
                           "处置: 重新执行一次完整升级即可修复 (会跳过电堆检查直接续升); " +
                           "若之前已到 S7 固化之后, 也可先断电重新上电尝试直接启动新固件。\n" +
                           "注意: 若后续 FCU 不轮询 BMS, 请在 BMS 页面使用\"归还 BMS 总线\"按钮自救。";
                }
                _ = Dispatcher.BeginInvoke(() => MessageBox.Show(
                    msg, "固件升级失败", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            catch (Exception ex)
            {
                Log($"升级异常: {ex}");
                Status("异常终止");
                await ExitNormalizationAsync();
            }
            finally
            {
                _busy = false;
                var oldCts = _cts;
                _cts = null;
                oldCts?.Dispose();          /* [全量终审 P2] 连续升级的 CTS 释放 */
                _ = Dispatcher.BeginInvoke(() =>
                {
                    btn_Start.IsEnabled = true;
                    btn_Cancel.IsEnabled = false;
                });
            }
        }

        /// <summary>统一退出规则 [设计 §4.4]: 停发 → 静默 6s → 切 9600 → 探活 → 正常断开(归还总线)
        /// 探活结果记录于 _lastExitAppAlive (true=APP存活已正常断开 / false=BL常驻已直接关串口 / null=未执行或重开失败),
        /// 供 catch 块生成准确的用户提示 [全量终审 P2: 弹窗统一]</summary>
        private async Task ExitNormalizationAsync()
        {
            _lastExitAppAlive = null;
            try
            {
                if (_bridge == null) return;

                Log("退出流程: 静默 6s (设备侧收帧超时复位)...");
                await Task.Delay(6000);

                _bridge.SetBlMode(false);
                _client.Reset();   /* 清升级客户端接收缓冲 [批次2审查 P2] */

                /* 9600 重开 (失败也继续, 让用户手动处理) */
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
