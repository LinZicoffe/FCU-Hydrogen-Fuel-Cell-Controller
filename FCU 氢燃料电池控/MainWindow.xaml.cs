// ======================================================================
// ⚠️ 功率曲线测试模式开关
// ======================================================================
// 启用测试模式：取消下面注释（删除前面的 //）
// 禁用测试模式：保持注释状态
// ======================================================================
//#define ENABLE_POWER_CHART_TEST
// ======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.Windows.Threading;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.ViewModels;
using FCU_氢燃料电池控.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;
using HC = HandyControl.Controls;

namespace FCU_氢燃料电池控
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private const string CustomerCompanyName = "瑞达新能源（广州）有限公司";
        private const string CustomerSystemName = "氢燃料电池标定系统";
        private const string CustomerProductVersion = "RIDI FCU/DCDC-2026/V1.5";

        // MVVM ViewModel
        private MainViewModel? _viewModel;

        // ❌ 已删除：private SerialPort? _serialPort; - 现在使用 SerialPortManager
        private DispatcherTimer? _timer;
        private DispatcherTimer? _pollingTimer;  // 电堆信息轮询定时器
        private BmsService? _bmsService;  // BMS Modbus 主站服务 [上位机优化计划 h-t2]
        private DispatcherTimer? _chartUpdateTimer;  // 图表数据采集定时器
        private bool _isSerialOpen = false;
        private ProtocolHandler? _protocolHandler;
        private bool _customerRequirementUiInitialized;

        // BMS 总线仲裁 (0x0D set_bms_poll) [SOC计划 §1.3]
        private volatile bool _bmsPollAckReceived;
        /// <summary>[审查修复 P2] 0x8D 确认成败分离: 收到即置 AckReceived(等待环退出),
        /// 仅结果码=成功才置 AckSuccess(固件对 en 非 0/1 或 Flash 写失败回非 0 结果码)。</summary>
        private volatile bool _bmsPollAckSuccess;
        /// <summary>[审查修复 P2] 接管是否已获 FCU 确认: BMS 轮询启动门控
        /// (未确认=FCU 可能仍自轮询 BMS, 双主站互毁)。断开时复位。</summary>
        private bool _bmsTakeoverConfirmed;
        /// <summary>[审查修复 P2] 总线交换在途互斥: 防泵送期间"断开→重连"嵌套重入接管
        /// 导致重复 0x0D 多写 Flash。归还路径不持此标志 (断开时归还必须放行)。</summary>
        private bool _bmsBusExchangeInProgress;
        /// <summary>[审查修复 P2] 归还在途: 接管泵送期间用户断开/关窗时中止接管,
        /// 保证 en=1 是总线上最后的 0x0D (防 en=0 反转 en=1 残留 Flash)。</summary>
        private volatile bool _bmsReturnInFlight;

        // 电堆信息轮询健康监测 [轮询修复]: 固件对 0x07 恒回 0x87 (fcu_protocol.c fcu_handle_read_stack_info),
        // 一个轮询周期内未收到响应即记一次未命中; 连续多次未命中时自动发广播探针鉴别
        // "设备地址不匹配" 与 "链路不通" (固件 fcu_protocol.c:527-530 接受广播地址 0x00)
        private volatile bool _awaitingStackInfo;      // 上一轮轮询尚无 0x87 响应 (RX 线程清除)
        private volatile int _stackInfoMissCount;      // 连续未命中计数
        private volatile bool _diagProbePending;       // 广播探针在途 (RX 线程清除)
        private volatile byte _diagProbeFunc;          // 广播探针响应的功能码
        private DispatcherTimer? _diagProbeTimer;      // 探针判定定时器 (一次性, UI 线程)
        private bool _diagDoneForConnection;           // 本连接内已自动诊断过 (防周期性重复弹窗)
        private const int StackInfoMissThreshold = 5;  // 触发诊断的连续未命中次数

        // 风扇手动运行控制(0x0F)：记录最近一次待确认命令，成功响应后再更新界面状态。
        private bool? _pendingFanManualEnable;
        private byte _pendingFanManualDuty;
        /// <summary>0x8F 风扇手动控制响应超时（秒）。旧固件无 0x0F 分支时静默丢帧，需明确反馈而非永远“等待确认”。</summary>
        private const int FanManualResponseTimeoutSeconds = 3;
        private DispatcherTimer? _fanManualResponseTimeoutTimer;
        /// <summary>本次连接内是否已从设备回读过压力参数；未读先写会整帧覆盖含未展示的红/黄阈值，需确认。</summary>
        private bool _pressureParamsLoadedFromDevice;

        // 固件升级 (Bootloader) 模式标志 [BL设计 §4.2]:
        // 互斥窗口 = S1 前置检查完成后启用, S8 切回 9600 并确认 APP 存活后解除;
        // 生效期间: 串口字节分流到升级客户端, SendFrame/ParameterManager 发送被拦截, 主 Tab 被禁用
        private volatile bool _blUpgradeMode;
        private volatile bool _upgradePrecheckMode;

        // 固件升级页所在 TabItem (升级期间保持可用, 其余主 Tab 禁用)
        private System.Windows.Controls.TabItem? _firmwareUpgradeTab;

        // [BL升级 终审P1] 抑制下一次连接事件自动下发的 0x0D en=0 (升级流程切回 9600 后由
        // 正常断开链路统一归还, 避免对新复位设备/BL 的多余 Flash 擦写)
        private bool _suppressNextAutoBmsPoll;

        /// <summary>AA55 完整帧到达事件 (固件升级页 S1 前置检查 / S8 存活确认用) [BL设计 §4.2]</summary>
        private event Action<byte[]>? BlAppFrameReceived;

        // 最近一次从 0x0701 回读到的 SOC 门控参数；默认值与固件默认配置一致
        // (beforepined@51bf7e2 fcu_protocol.c:118-124: 60/61/60, SOC<60 被动自启 / >61 自动停)。
        private int _socStartMax = 60;
        private int _socStopHigh = 61;
        private int _socStopMin = 60;

        /// <summary>0x0502 lock_coefficient(单位0.001)透传缓存：固件保留字段，控件置灰，
        /// 写入时按回读值原样回传（初值 900=固件默认，回读时更新，依据 fcu_protocol.h 保留字段注释）</summary>
        private ushort _lastLockFactor0P001 = 900;

        /// <summary>待确认的 0x0504 拉载手动指令名（null=无在途指令）；0x82 响应据此路由到专用处理器</summary>
        private string? _pendingLoadManualCmd;
        private DispatcherTimer? _loadManualResponseTimeoutTimer;
        private const int LoadManualResponseTimeoutSeconds = 5;
        private SocParams? _pendingSocWrite;
        private byte? _pendingSocWriteDeviceAddress;
        private string? _pendingSocWritePortName;
        private byte? _pendingSocReadDeviceAddress;
        private bool _socWriteReadbackPending;
        private bool _socParamsAwaitingRestart;
        private bool _socWriteResultUnknown;
        private DispatcherTimer? _socResponseTimeoutTimer;
        private const int SocResponseTimeoutSeconds = 5;

        // 0x0202 扩展时序写入的 0x82 归属（pending+超时，仿 SOC 模式）。
        // 0x82 响应数据域仅 1 字节错误码、无参数 ID，多帧连发无法归属——pending 仅由
        // btn_SetValveTimingParams_Click 登记，消费点在 HandleLoadManualResponse 的
        // 0x82 回落分支（0x8E 直连 HandleSetParamResponse，不触碰本 pending）。
        private DateTime? _pendingValveTimingSet;
        private DispatcherTimer? _valveTimingResponseTimeoutTimer;
        private const int ValveTimingResponseTimeoutSeconds = 2;
#if ENABLE_LICENSE_FEATURE
        private readonly LicenseService? _licenseService;
#endif

        // 图表相关字段
        private readonly ObservableCollection<double> _inputPowerValues = new ObservableCollection<double>();
        private readonly ObservableCollection<double> _outputPowerValues = new ObservableCollection<double>();
        private readonly ObservableCollection<DateTime> _timeValues = new ObservableCollection<DateTime>();
        private const int MaxDataPoints = 600;  // 最大数据点数（60秒窗口 × 10点/秒）
        private double _currentInputVoltage = 0;
        private double _currentInputCurrent = 0;
        private double _currentOutputVoltage = 0;
        private double _currentOutputCurrent = 0;

        // 历史配方页已隐藏且不再初始化；仅保留字段供旧代码可编译回退。
        private RecipeService? _recipeService = null;
        private string? _selectedRecipeId;

#if ENABLE_POWER_CHART_TEST
        // 测试模式字段
        private readonly Random _random = new Random();
        private DispatcherTimer? _testDataUpdateTimer;
        private double _testInputPower = 0;
        private double _testOutputPower = 0;
        private DateTime _lastTestDataUpdate = DateTime.Now;
#endif

        // 图表属性（用于XAML绑定）
        public ISeries[] PowerSeries { get; set; } = Array.Empty<ISeries>();
        public Axis[] XAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] YAxes { get; set; } = Array.Empty<Axis>();

        /// <summary>
        /// 构造函数
        /// </summary>
#if ENABLE_LICENSE_FEATURE
        public MainWindow(string companyName, LicenseService licenseService)
#else
        public MainWindow(string companyName)
#endif
        {
            InitializeComponent();
            _customerRequirementUiInitialized = true;
            InitializeCustomerRequirementUi();
#if ENABLE_LICENSE_FEATURE
            _licenseService = licenseService;
#endif
            // 系统标题固定显示；公司名称放在独立的白色品牌行和底部版权信息中。
            Title = CustomerSystemName;
            tbk_ApplicationName.Text = CustomerCompanyName;
            tbk_SystemName.Text = CustomerSystemName;
            tbk_ProductVersion.Text = CustomerProductVersion;
            tbk_Copyright.Text = $"版权归{CustomerCompanyName}所有";
            
            // ==================== MVVM初始化 ====================
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // 连接ViewModel的SerialPortManager到协议处理
            var serialPortManager = _viewModel.GetSerialPortManager();
            serialPortManager.DataReceived += SerialPort_DataReceived;
            serialPortManager.ConnectionStateChanged += OnSerialConnectionChanged;
            serialPortManager.FrameSent += OnFrameSent;
            _viewModel.BeforeSerialClose += OnBeforeSerialClose;
            // VM 无 UI 引用，本地校验失败经事件弹 Growl，与 code-behind 参数组的反馈方式一致
            _viewModel.ParameterValidationFailed += (_, msg) => HC.Growl.Warning(msg);
            // 多机共总线切换目标设备后，作废「已读取压力」豁免，防止跨设备静默覆盖隐藏红/黄阈值
            _viewModel.DeviceAddressChanged += (_, _) =>
            {
                _pressureParamsLoadedFromDevice = false;
                _bmsExchangeGeneration++;
                ResetFirmwareMainSession();
                _bmsService?.StopPolling();
                _bmsTakeoverConfirmed = false;
                if (_bmsBusMayBeOwned) OnBeforeSerialClose();
                chk_DirectBmsMode.IsChecked = false;
            };
            // ==================== MVVM初始化结束 ====================
            
            // 协议处理初始化（保留）
            InitializeProtocolHandler();
            
            // 轮询定时器初始化（保留）
            InitializePollingTimer();
            InitializeSocResponseTimeoutTimer();
            InitializeValveTimingResponseTimeoutTimer();
            InitializeFanManualResponseTimeoutTimer();
            InitializeLoadManualResponseTimeoutTimer();

            // BMS Modbus 主站服务初始化 [上位机优化计划 h-t4]
            _bmsService = new BmsService(serialPortManager);
            // 总线仲裁第二条件: 与固件 fcu_modbus.c 对齐，AA55 半接收状态下不插入 Modbus 请求
            _bmsService.IsAa55Idle = () => _protocolHandler?.IsIdle ?? true;
            // 总线仲裁第三条件 [审查修复 P1②]: 自身 0x07 在途(已发未收 0x87)期间不插入 Modbus 请求,
            // 否则会与 FCU 的响应发送相撞——"总线静默"只看收, 看不到自己刚发出的请求
            _bmsService.IsAa55RequestInFlight = () => _awaitingStackInfo;
            _bmsService.DataUpdated += OnBmsDataUpdated;
            _bmsService.StatusChanged += msg => Dispatcher.Invoke(() => UpdateStatus(msg));
            _bmsService.ErrorOccurred += msg => Dispatcher.Invoke(() => { HC.Growl.Warning(msg); UpdateStatus(msg); });
            _bmsService.MosWriteCompleted += OnBmsMosWriteCompleted;

            // 固件升级页初始化 [BL设计 §4.2 集成点]: 注入串口管理器与宿主桥接
            _firmwareUpgradeTab = FirmwareUpgradeView.Parent as System.Windows.Controls.TabItem;
            FirmwareUpgradeView.Initialize(serialPortManager, new BlHostBridgeImpl(this));
            // 升级期间拦截 ParameterManager 的 AA55 发送 (MVVM 命令 CanExecute 只查 IsConnected 拦不住)
            _viewModel.ParameterManager.SendBlocked = () => _blUpgradeMode || _upgradePrecheckMode;
            InitializeFirmwareMain();

            // 图表初始化（保留）
            InitializePowerChart();
            InitializeChartUpdateTimer();
            
            // 授权码功能暂时停用；启用编译常量后恢复状态显示。
#if ENABLE_LICENSE_FEATURE
            Loaded += MainWindow_Loaded;
#endif

        }

        /// <summary>
        /// 初始化 2026-08-16 客户需求中仅依赖上位机的校验与时序预览。
        /// 这些预览不代表设备实时状态，也不会复用旧固件协议写入新字段。
        /// </summary>
        private void InitializeCustomerRequirementUi()
        {
            UpdateStartupTimingPreview();
            UpdateShutdownTimingPreview();
        }

        private void ValveTimingInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_customerRequirementUiInitialized ||
                startupValveTimingPreview == null || shutdownValveTimingPreview == null)
            {
                return;
            }

            UpdateStartupTimingPreview();
            UpdateShutdownTimingPreview();
            UpdateShutdownTotal();
        }

        private void btn_ValidateValveTiming_Click(object sender, RoutedEventArgs e)
        {
            bool startupValid = UpdateStartupTimingPreview();
            bool shutdownValid = UpdateShutdownTimingPreview();
            if (startupValid && shutdownValid)
            {
                HC.Growl.Success("开机和关机时序校验通过；当前仅完成本地预览，未写入FCU");
            }
            else
            {
                HC.Growl.Warning("时序参数存在错误，请根据红色提示修改");
            }
        }

        /// <summary>
        /// 开机时序预览（固件实测语义：t=0 为 12V 上电，排水/氢阀两轨各自经领启延时启动、并行推进）。
        /// 排水轨 开①(0x0201)→关①(0x0202)→开②(0x0201)→关②(0x0202)→开③(0x0202)；
        /// 氢阀轨 领启后初始关闭(h2c1)→开(h2o)→关(h2c2)→常开（HOLD_OPEN）。
        /// 预算：固件不校验总长，开机序列 20s 未完成撞 DCDC 门控 FAULT（4.0s 起检查）。
        /// </summary>
        private bool UpdateStartupTimingPreview()
        {
            if (startupValveTimingPreview == null || tbk_StartupTimingStatus == null)
            {
                return false;
            }

            const double maxInput = 60.0;
            var fields = new (TextBox Box, string Name)[]
            {
                (tbx_StartupDrainLeadDelay, "排水领启延时"),
                (tbx_StartupDrainFirstOpen, "电堆吹扫①"),
                (tbx_StartupDrainFirstClose, "吹扫停止①"),
                (tbx_StartupDrainSecondOpen, "电堆吹扫②"),
                (tbx_StartupDrainThirdInterval, "吹扫停止②"),
                (tbx_StartupDrainThirdOpen, "电堆吹扫③"),
                (tbx_StartupH2LeadDelay, "氢阀领启延时"),
                (tbx_StartupH2FirstDelay, "氢阀初始关闭"),
                (tbx_StartupH2FirstOpen, "氢阀打开①"),
                (tbx_StartupH2ReopenDelay, "氢阀再开前关闭")
            };

            double[] values = new double[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                if (!TryReadTimingValue(fields[i].Box, fields[i].Name, out values[i], out string error, maxInput))
                {
                    SetTimingError(startupValveTimingPreview, tbk_StartupTimingStatus, error);
                    return false;
                }
            }

            // values: [0]排水领启 [1]吹扫①(0x0201) [2]停止① [3]吹扫②(0x0201) [4]停止② [5]吹扫③ [6]氢阀领启 [7]初始关闭 [8]打开① [9]再开前关闭
            double drainLead = values[0];
            double drainClose1 = drainLead + values[1];
            double drainOpen2 = drainClose1 + values[2];
            double drainClose2 = drainOpen2 + values[3];
            double drainOpen3 = drainClose2 + values[4];
            double drainEnd = drainOpen3 + values[5];

            double h2Lead = values[6];
            double h2Open = h2Lead + values[7];
            double h2Close = h2Open + values[8];
            double h2End = h2Close + values[9];

            if (drainLead >= h2Lead)
            {
                SetTimingError(startupValveTimingPreview, tbk_StartupTimingStatus,
                    $"排水领启延时({drainLead:0.00}s)必须严格小于氢阀领启延时({h2Lead:0.00}s)，固件会拒绝写入");
                return false;
            }

            double total = Math.Max(Math.Max(drainEnd, h2End), 0.01);
            if (total > 20.0)
            {
                SetTimingError(startupValveTimingPreview, tbk_StartupTimingStatus,
                    $"开机序列总长 {total:0.00}s 超出 DCDC 门控 20s 预算：固件不校验总长，超限将触发 0x0006 故障停机，请压缩时序");
                return false;
            }

            startupValveTimingPreview.SetTimeline(total,
                new ValveTrack(false,
                    new[] { h2Open, h2Close, h2End },
                    new[] { "开①", "关①", "常开" }),
                new ValveTrack(false, new[]
                {
                    drainLead, drainClose1, drainOpen2, drainClose2, drainOpen3, drainEnd
                }, new[] { "开①", "关①", "开②", "关②", "开③", "关③" }));

            tbk_StartupTimingStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            tbk_StartupTimingStatus.Text =
                $"有效预览：t=0 为 12V 上电；排水轨 {drainLead:0.00}s 领启、{drainEnd:0.00}s 完成后进入正常排水；" +
                $"氢阀轨 {h2Lead:0.00}s 领启、{h2End:0.00}s 起常开；总长 {total:0.00}s 在 DCDC 门控 20s 预算内（固件按 100ms 调度，各段有 0~0.1s 量化余量）。";
            return true;
        }

        /// <summary>
        /// 关机时序预览（固件实测语义：t=0 氢阀 purge 与排水连续保持同拍并行启动）。
        /// 氢阀轨 开①(0x0E purge_h2_open1)→关(purge_h2_close)→开②(purge_h2_open2)→终关（固件 DONE 关阀）；
        /// 排水轨连续开启 总长 = 关机排水保持(0x0E) + 关机排空保持(0x0202)，直接由两输入框求和，
        /// 不经 tbx_ShutdownTotal（单项合法仅预算超限时不得被 60s 上限误报）。
        /// 预算：固件不校验总长，关机 30s 强制下电截断。
        /// </summary>
        private bool UpdateShutdownTimingPreview()
        {
            if (shutdownValveTimingPreview == null || tbk_ShutdownTimingStatus == null)
            {
                return false;
            }

            const double maxInput = 60.0;
            var fields = new (TextBox Box, string Name)[]
            {
                (tbx_ShutdownH2Open1, "氢阀补气①"),
                (tbx_ShutdownH2Close2, "氢阀关闭②"),
                (tbx_ShutdownH2Open2, "氢阀补气②"),
                (tbx_PurgeDrainHoldSec, "关机排水保持"),
                (tbx_ShutdownEmptySec, "关机排空保持")
            };

            double[] values = new double[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                if (!TryReadTimingValue(fields[i].Box, fields[i].Name, out values[i], out string error, maxInput))
                {
                    SetTimingError(shutdownValveTimingPreview, tbk_ShutdownTimingStatus, error);
                    return false;
                }
            }

            // values: [0]补气① [1]关闭② [2]补气② [3]排水保持 [4]排空保持
            double h2Close1 = values[0];
            double h2Open2 = h2Close1 + values[1];
            double h2End = h2Open2 + values[2];
            double drainTotal = values[3] + values[4];

            double total = Math.Max(Math.Max(drainTotal, h2End), 0.01);
            if (drainTotal > 30.0 || h2End > 30.0)
            {
                SetTimingError(shutdownValveTimingPreview, tbk_ShutdownTimingStatus,
                    $"关机时序超出 30s 强制下电预算：排水 {drainTotal:0.00}s / 氢阀 {h2End:0.00}s——固件不校验总长，超限将被强制下电截断");
                return false;
            }

            shutdownValveTimingPreview.SetTimeline(total,
                new ValveTrack(true,
                    new[] { h2Close1, h2Open2, h2End },
                    new[] { "关②", "开②", "终关" }),
                new ValveTrack(true, new[] { drainTotal }, new[] { "关" }));

            tbk_ShutdownTimingStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            tbk_ShutdownTimingStatus.Text =
                $"有效预览：t=0 同时开氢阀（purge）与排水阀；排水连续 {drainTotal:0.00}s（保持+排空），" +
                $"氢阀 purge {h2End:0.00}s 终关；双路完成后断 12V（固件关机 30s 强制下电，各段有 0~0.1s 量化余量）。";
            return true;
        }

        private static bool TryReadTimingValue(TextBox box, string fieldName, out double value,
            out string error, double maxSeconds = 9.99)
        {
            string text = box.Text?.Trim() ?? string.Empty;
            bool parsed = double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            if (!parsed)
            {
                error = $"{fieldName}“{text}”不是合法数值";
                return false;
            }

            decimal decimalValue;
            try
            {
                decimalValue = Convert.ToDecimal(value);
            }
            catch (OverflowException)
            {
                error = $"{fieldName}超出可用范围";
                return false;
            }

            if (value < 0 || value > maxSeconds)
            {
                error = $"{fieldName}超出范围，允许 0.00～{maxSeconds:0.##}s";
                return false;
            }

            if (decimal.Round(decimalValue, 2, MidpointRounding.AwayFromZero) != decimalValue)
            {
                error = $"{fieldName}最多支持两位小数";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void SetTimingError(ValveTimingPreview preview, TextBlock status, string error)
        {
            preview.SetError(error);
            status.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            status.Text = "参数错误：" + error;
        }

        private void btn_ValidateSocParams_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSocParamsFromUi(out _))
            {
                return;
            }

            tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            tbk_SocValidationStatus.Text = "本地校验通过；三个SOC阈值均为1～100%，电池容量为正整数Ah。";
            HC.Growl.Success("SOC参数本地校验通过");
        }

        private bool TryReadSocParamsFromUi(out SocParams parameters)
        {
            parameters = new SocParams();

            if (!TryParseRange(tbx_OverviewSocStartMax, "被动自启阈值", 1, 100, "%", out ushort startMax) ||
                !TryParseRange(tbx_OverviewSocStopHigh, "运行停机上限", 1, 100, "%", out ushort stopHigh) ||
                !TryParseRange(tbx_OverviewSocStopMin, "关机充电下限(保留)", 1, 100, "%", out ushort stopMin) ||
                !TryParseRange(tbx_OverviewBatteryCapacity, "电池容量", 1, ushort.MaxValue, "Ah", out ushort capacity))
            {
                SetSocValidationError("三个SOC阈值必须是1～100的整数，电池容量必须是正整数Ah");
                return false;
            }

            /* 固件新增交叉校验 (beforepined@51bf7e2 fcu_protocol.c:1838-1843):
             * soc_start_max >= soc_stop_high 直接拒绝 (0x02), 主机侧前置拦截并给出原因 */
            if (startMax >= stopHigh)
            {
                SetSocValidationError($"被动自启阈值({startMax}%)必须小于运行停机上限({stopHigh}%)：两者构成启动滞回（SOC<下限自动开机、>上限自动停机），相等或反置会被固件拒绝");
                HC.Growl.Warning($"被动自启阈值({startMax}%)必须小于运行停机上限({stopHigh}%)");
                tbx_OverviewSocStartMax.Focus();
                tbx_OverviewSocStartMax.SelectAll();
                return false;
            }

            parameters.StartMax = (byte)startMax;
            parameters.StopHigh = (byte)stopHigh;
            parameters.StopMin = (byte)stopMin;
            parameters.BatteryCapacity = capacity;
            return true;
        }

        private void btn_ReadSocParams_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSocReadDeviceAddress.HasValue)
            {
                HC.Growl.Warning("已有SOC读取正在等待响应，请勿重复发送");
                return;
            }

            if (_pendingSocWrite != null && !_socWriteReadbackPending)
            {
                HC.Growl.Warning("SOC写入尚未收到FCU确认，暂不发送读取命令");
                return;
            }

            if (!SerialPortHelper.CheckSerialPortOpen(_isSerialOpen))
            {
                return;
            }

            byte deviceAddress;
            if (_socWriteReadbackPending && _pendingSocWriteDeviceAddress.HasValue)
            {
                deviceAddress = _pendingSocWriteDeviceAddress.Value;
            }
            else if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out deviceAddress))
            {
                return;
            }

            if (!SendSocReadConfigCommand(deviceAddress))
            {
                SetSocValidationError("SOC参数读取帧发送失败，未进入等待响应状态");
                return;
            }

            UpdateStatus("已发送读取BMS/SOC参数命令");
        }

        private bool SendSocReadConfigCommand(byte deviceAddress)
        {
            _pendingSocReadDeviceAddress = deviceAddress;
            bool sent = SendReadConfigCommand(deviceAddress,
                (byte)(ProtocolConstants.PARAM_TYPE_SOC >> 8),
                (byte)(ProtocolConstants.PARAM_TYPE_SOC & 0xFF));

            if (!sent)
            {
                _pendingSocReadDeviceAddress = null;
                return false;
            }

            RestartSocResponseTimeout();
            return true;
        }

        private void btn_SetSocParams_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSocReadDeviceAddress.HasValue)
            {
                HC.Growl.Warning("SOC读取尚未完成，请等待响应后再写入");
                return;
            }

            if (_pendingSocWrite != null)
            {
                HC.Growl.Warning(_socWriteReadbackPending
                    ? "正在核对上一笔SOC写入，请等待回读完成"
                    : "上一笔SOC写入尚未收到FCU确认，请勿重复发送");
                return;
            }

            if (!TryReadSocParamsFromUi(out SocParams parameters))
            {
                return;
            }

            if (!SerialPortHelper.CheckSerialPortOpen(_isSerialOpen) ||
                !SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte deviceAddress))
            {
                return;
            }

            byte[] data = ResponseParser.BuildSocParamsData(parameters);
            _pendingSocWrite = parameters;
            _pendingSocWriteDeviceAddress = deviceAddress;
            _pendingSocWritePortName = _viewModel?.SelectedPortName;
            _socWriteReadbackPending = false;
            if (!SendFrame(BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_SOC_PARAMS, data)))
            {
                _pendingSocWrite = null;
                _pendingSocWriteDeviceAddress = null;
                _pendingSocWritePortName = null;
                SetSocValidationError("SOC参数写入帧发送失败，未进入等待响应状态");
                return;
            }

            RestartSocResponseTimeout();
            tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
            tbk_SocValidationStatus.Text = "已发送SOC参数写入命令，等待FCU确认。";
            UpdateStatus("已发送BMS/SOC参数写入并固化命令");
        }

        private void SetSocValidationError(string error)
        {
            tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            tbk_SocValidationStatus.Text = "参数错误：" + error;
        }
        
        /// <summary>
        /// 串口连接状态改变事件处理
        /// </summary>
        private void OnSerialConnectionChanged(object? sender, bool isConnected)
        {
            _bmsExchangeGeneration++; // 断线后即使重连同一目标，也不能复活先前的接管等待。
            _isSerialOpen = isConnected;
            
            if (isConnected)
            {
                if (_socWriteReadbackPending &&
                    (_pendingSocWriteDeviceAddress.HasValue || _pendingSocWritePortName != null))
                {
                    bool samePort = string.Equals(_pendingSocWritePortName,
                        _viewModel?.SelectedPortName, StringComparison.OrdinalIgnoreCase);
                    bool sameAddress = _pendingSocWriteDeviceAddress.HasValue &&
                        SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(),
                            out byte connectedAddress, showError: false) &&
                        connectedAddress == _pendingSocWriteDeviceAddress.Value;
                    if (!samePort || !sameAddress)
                    {
                        _pendingSocWrite = null;
                        _pendingSocWriteDeviceAddress = null;
                        _pendingSocWritePortName = null;
                        _pendingSocReadDeviceAddress = null;
                        _socWriteReadbackPending = false;
                        tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                        tbk_SocValidationStatus.Text =
                            "重连的串口或设备地址与原SOC写入目标不一致，已取消自动核对；原设备参数仍待重启生效。";
                    }
                }

                // 串口打开时，启动图表采集
                _chartUpdateTimer?.Start();

                // 新连接的设备未知，压力参数须重新读取后才能免确认整帧写入
                _pressureParamsLoadedFromDevice = false;
                tbk_FanManualMode.Text = "风扇当前模式以FCU侧为准";
                tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));

                // [SOC计划 §1.3] 会话级仲裁: 上位机接管 BMS 总线, 停固件 Modbus 轮询
                // (固件每次写Flash, 禁止逐次握手; 关轮询后固件走离线宽松策略, 不会超时ABORT)
                // [BL升级 终审P1] 升级流程驱动的 9600 重开(S8/退出)不自动下发 en=0:
                // 此时设备可能刚复位/仍在 BL, 多余的 en=0 会造成参数页无谓擦写与残留窗口
                if (_suppressNextAutoBmsPoll)
                {
                    _suppressNextAutoBmsPoll = false;
                }
                else if (!_blUpgradeMode && !_upgradePrecheckMode && DirectBmsMode)
                {
                    /* [审查修复 P2] 升级入口 SetBlMode(true) 的 57600 重开同样触发连接事件:
                     * 对端是 Bootloader/波特率切换中的设备, 0x0D 无意义且 SendFrame 被
                     * _blUpgradeMode 拦截, 接管确认必然 3 次落空 + 误报告警, 直接跳过。
                     * 正常连接仍走接管: 单发 0x0D 有概率落入固件 BMS 轮询等待窗口被吞,
                     * 因此用 0x8D 确认 + 有界重试 (见 TakeOverBmsBusWithConfirm) */
                    TakeOverBmsBusWithConfirm();
                }
            }
            else
            {
                StopSocResponseTimeout();
                ResetFirmwareMainSession();

                // 串口关闭时，停止所有定时器
                _chartUpdateTimer?.Stop();
                
                // 停止电堆信息轮询
                if (_pollingTimer != null && _pollingTimer.IsEnabled)
                {
                    _pollingTimer.Stop();
                }

                // 轮询健康监测状态复位 (探针定时器一并停止, 防跨会话残留误报)
                _awaitingStackInfo = false;
                _stackInfoMissCount = 0;
                _diagProbePending = false;
                _diagDoneForConnection = false;
                StopStackInfoLinkDiagnosis();

                // [审查修复 P2] 接管状态随连接失效, 下次连接重新确认
                _bmsTakeoverConfirmed = false;
                
                // 停止BMS Modbus轮询并清显示
                _bmsService?.StopPolling();
                ResetBmsDisplay();
                _pendingFanManualEnable = null;
                StopFanManualResponseTimeout();
                _pendingLoadManualCmd = null;
                StopLoadManualResponseTimeout();
                _pendingValveTimingSet = null;
                StopValveTimingResponseTimeout();
                if (!_socWriteReadbackPending)
                {
                    bool hadUnconfirmedSocWrite = _pendingSocWrite != null;
                    _pendingSocWrite = null;
                    _pendingSocWriteDeviceAddress = null;
                    _pendingSocWritePortName = null;
                    _pendingSocReadDeviceAddress = null;
                    if (hadUnconfirmedSocWrite)
                    {
                        // 新一笔写入是否到达设备未知时，不能继续把更早的已确认写入
                        // 当作唯一待重启值；下一次重启实际采用哪组 Flash 值已无法确定。
                        _socParamsAwaitingRestart = false;
                        _socWriteResultUnknown = true;
                        tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                        tbk_SocValidationStatus.Text =
                            "连接已断开，SOC写入未收到确认；设备结果和下次重启生效值未知，输入保持不变。";
                    }
                }
                else
                {
                    _pendingSocReadDeviceAddress = null;
                    tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                    tbk_SocValidationStatus.Text = "SOC写入已确认，但自动回读因断线中断；重连后请点击读取SOC参数完成核对。";
                }
                tbk_FanManualMode.Text = "未连接（风扇当前模式以FCU侧为准）";
                tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));
            }
        }

#if ENABLE_LICENSE_FEATURE
        /// <summary>
        /// 窗口加载事件
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLicenseStatusDisplay();
        }
#endif

        #region 初始化方法

        

        /// <summary>
        /// 初始化定时器
        /// </summary>
        private void InitializeTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        /// <summary>
        /// 初始化轮询定时器
        /// </summary>
        private void InitializePollingTimer()
        {
            _pollingTimer = new DispatcherTimer();
            _pollingTimer.Interval = TimeSpan.FromSeconds(1); // 默认1秒
            _pollingTimer.Tick += PollingTimer_Tick;
            // 默认不启动，由用户点击按钮启动
        }

        /// <summary>
        /// SOC 读写使用同一个单次超时计时器；响应到达、断线或窗口关闭时立即停止。
        /// </summary>
        private void InitializeSocResponseTimeoutTimer()
        {
            _socResponseTimeoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(SocResponseTimeoutSeconds)
            };
            _socResponseTimeoutTimer.Tick += SocResponseTimeoutTimer_Tick;
        }

        private void RestartSocResponseTimeout()
        {
            _socResponseTimeoutTimer?.Stop();
            _socResponseTimeoutTimer?.Start();
        }

        private void StopSocResponseTimeout()
        {
            _socResponseTimeoutTimer?.Stop();
        }

        /// <summary>
        /// 0x0202 扩展时序写入响应超时计时器：响应到达（0x82 回落分支消费）即停表；
        /// 超时说明固件未回包（旧固件对 27B 的 0x03 帧静默丢弃）或线路异常。
        /// </summary>
        private void InitializeValveTimingResponseTimeoutTimer()
        {
            _valveTimingResponseTimeoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(ValveTimingResponseTimeoutSeconds)
            };
            _valveTimingResponseTimeoutTimer.Tick += (s, e) =>
            {
                _valveTimingResponseTimeoutTimer?.Stop();
                if (_pendingValveTimingSet == null)
                {
                    return;
                }
                _pendingValveTimingSet = null;
                UpdateStatus("扩展时序(0x0202)写入超时");
                HC.Growl.Warning("2s 未收到设置响应——固件可能不支持 0x0202（需包含0x0202扩展的固件，最新main已包含）或线路异常");
            };
        }

        private void RestartValveTimingResponseTimeout()
        {
            _valveTimingResponseTimeoutTimer?.Stop();
            _valveTimingResponseTimeoutTimer?.Start();
        }

        private void StopValveTimingResponseTimeout()
        {
            _valveTimingResponseTimeoutTimer?.Stop();
        }

        /// <summary>
        /// 0x8F 风扇手动控制单次超时计时器；响应到达、断线或窗口关闭时立即停止。
        /// </summary>
        private void InitializeFanManualResponseTimeoutTimer()
        {
            _fanManualResponseTimeoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(FanManualResponseTimeoutSeconds)
            };
            _fanManualResponseTimeoutTimer.Tick += FanManualResponseTimeoutTimer_Tick;
        }

        private void StartFanManualResponseTimeout()
        {
            _fanManualResponseTimeoutTimer?.Stop();
            _fanManualResponseTimeoutTimer?.Start();
        }

        private void StopFanManualResponseTimeout()
        {
            _fanManualResponseTimeoutTimer?.Stop();
        }

        private void FanManualResponseTimeoutTimer_Tick(object? sender, EventArgs e)
        {
            StopFanManualResponseTimeout();

            if (_pendingFanManualEnable == null)
            {
                return;
            }

            _pendingFanManualEnable = null;
            tbk_FanManualMode.Text = "未收到FCU确认（固件可能不支持0x0F或链路异常），当前风扇模式未知";
            tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            UpdateStatus("风扇手动控制响应超时");
        }

        /// <summary>
        /// 0x0504 拉载手动控制单次超时计时器；响应到达、断线或窗口关闭时立即停止。
        /// </summary>
        private void InitializeLoadManualResponseTimeoutTimer()
        {
            _loadManualResponseTimeoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(LoadManualResponseTimeoutSeconds)
            };
            _loadManualResponseTimeoutTimer.Tick += LoadManualResponseTimeoutTimer_Tick;
        }

        private void StartLoadManualResponseTimeout()
        {
            _loadManualResponseTimeoutTimer?.Stop();
            _loadManualResponseTimeoutTimer?.Start();
        }

        private void StopLoadManualResponseTimeout()
        {
            _loadManualResponseTimeoutTimer?.Stop();
        }

        private void LoadManualResponseTimeoutTimer_Tick(object? sender, EventArgs e)
        {
            StopLoadManualResponseTimeout();

            if (_pendingLoadManualCmd == null)
            {
                return;
            }

            string cmd = _pendingLoadManualCmd;
            _pendingLoadManualCmd = null;
            UpdateStatus($"拉载指令[{cmd}]响应超时");
            HC.Growl.Warning($"拉载指令[{cmd}] {LoadManualResponseTimeoutSeconds} 秒内未收到FCU确认，执行结果未知（请检查链路或查看设备状态）");
        }

        private void SocResponseTimeoutTimer_Tick(object? sender, EventArgs e)
        {
            StopSocResponseTimeout();

            if (_pendingSocReadDeviceAddress.HasValue)
            {
                _pendingSocReadDeviceAddress = null;

                if (_socWriteReadbackPending && _pendingSocWrite != null)
                {
                    tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                    tbk_SocValidationStatus.Text =
                        "SOC写入已确认，但自动回读5秒内未响应；写入快照已保留，可再次点击读取SOC参数核对。";
                    UpdateStatus("BMS/SOC写入后自动回读超时，可手动重试读取");
                    HC.Growl.Warning("SOC写入已确认，但自动回读超时；请重试读取");
                    return;
                }

                tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                tbk_SocValidationStatus.Text = "读取SOC参数5秒内未收到响应，可重试读取。";
                UpdateStatus("读取BMS/SOC参数超时");
                HC.Growl.Warning("读取SOC参数超时");
                return;
            }

            if (_pendingSocWrite != null && !_socWriteReadbackPending)
            {
                _pendingSocWrite = null;
                _pendingSocWriteDeviceAddress = null;
                _pendingSocWritePortName = null;
                _socParamsAwaitingRestart = false;
                _socWriteResultUnknown = true;
                tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                tbk_SocValidationStatus.Text =
                    "SOC写入5秒内未收到确认；设备结果和下次重启生效值未知，可重新读取Flash值核查。";
                UpdateStatus("BMS/SOC参数写入响应超时，设备结果未知");
                HC.Growl.Warning("SOC写入响应超时，设备结果未知");
            }
        }

        /// <summary>
        /// 初始化协议处理器
        /// </summary>
        private void InitializeProtocolHandler()
        {
            _protocolHandler = new ProtocolHandler();
            _protocolHandler.FrameReceived += OnFrameReceived;
            _protocolHandler.FrameValidationFailed += OnFrameValidationFailed;
        }

        /// <summary>
        /// 初始化功率图表
        /// </summary>
        private void InitializePowerChart()
        {
            // 配置输入功率曲线（蓝色）
            var inputPowerSeries = new LineSeries<double>
            {
                Values = _inputPowerValues,
                Name = "Input Power",  // 图例在XAML中自定义显示，这里使用简单名称
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0,  // 不显示数据点
                LineSmoothness = 0.5  // 平滑曲线
            };

            // 配置输出功率曲线（橙红色）
            var outputPowerSeries = new LineSeries<double>
            {
                Values = _outputPowerValues,
                Name = "Output Power",  // 图例在XAML中自定义显示，这里使用简单名称
                Stroke = new SolidColorPaint(SKColors.OrangeRed) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0,  // 不显示数据点
                LineSmoothness = 0.5  // 平滑曲线
            };

            PowerSeries = new ISeries[] { inputPowerSeries, outputPowerSeries };

            // 配置X轴（时间轴）
            XAxes = new Axis[]
            {
                new Axis
                {
                    //Name = "Time",
                    NamePaint = new SolidColorPaint(SKColors.Black),
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    TextSize = 12,
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 },
                    ShowSeparatorLines = true,
                    MinStep = 1,  // 设置最小步长，避免过密标签
                    ForceStepToMin = false,  // 允许自动调整步长
                    Labeler = value => {
                        int index = (int)value;
                        // 只在有有效数据时显示标签
                        if (index >= 0 && index < _timeValues.Count && _timeValues.Count > 0)
                        {
                            var time = _timeValues[index];
                            return time.ToString("HH:mm:ss");
                        }
                        return string.Empty;  // 返回空字符串（不能返回null）
                    }
                }
            };

            // 配置Y轴（功率轴）
            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "P (W)",
                    NamePaint = new SolidColorPaint(SKColors.Black),
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    TextSize = 12,
                    MinLimit = 0,
                    MaxLimit = 1200,
                    MinStep = 200,                    // 强制最小刻度间隔为200W，确保显示完整范围
                    ForceStepToMin = true,           // 强制使用MinStep，不允许自动调整
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 },
                    ShowSeparatorLines = true
                }
            };

            // 设置数据上下文
            PowerChart.DataContext = this;
        }

        /// <summary>
        /// 初始化图表数据采集定时器
        /// </summary>
        private void InitializeChartUpdateTimer()
        {
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(100);  // 10 Hz (100 ms/点)
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
            _chartUpdateTimer.Start();  // 程序启动后自动开始采集
        }

        /// <summary>
        /// 图表数据采集定时器Tick事件
        /// </summary>
        private void ChartUpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
#if ENABLE_POWER_CHART_TEST
                // ========== 测试模式：每0.5秒更新一次随机功率数据 ==========
                if ((DateTime.Now - _lastTestDataUpdate).TotalMilliseconds >= 500)
                {
                    // 生成新的随机功率值（0-1200W范围）
                    _testInputPower = _random.NextDouble() * 1200;
                    _testOutputPower = _random.NextDouble() * 1200;
                    _lastTestDataUpdate = DateTime.Now;
                }

                // 使用测试数据
                double inputPower = _testInputPower;
                double outputPower = _testOutputPower;
#else
                // ========== 正常模式：使用实际电堆数据 ==========
                // 计算输入功率和输出功率
                double inputPower = _currentInputVoltage * _currentInputCurrent;
                double outputPower = _currentOutputVoltage * _currentOutputCurrent;
#endif

                // 添加新数据点
                _inputPowerValues.Add(inputPower);
                _outputPowerValues.Add(outputPower);
                _timeValues.Add(DateTime.Now);

                // 保持数据点数量不超过最大值
                if (_inputPowerValues.Count > MaxDataPoints)
                {
                    _inputPowerValues.RemoveAt(0);
                    _outputPowerValues.RemoveAt(0);
                    _timeValues.RemoveAt(0);
                }
            }
            catch
            {
                // 静默处理异常
            }
        }

        /// <summary>
        /// 定时器Tick事件
        /// </summary>
        private void Timer_Tick(object? sender, EventArgs e)
        {
            tbk_Time.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 更新状态栏（保留用于兼容，实际通过ViewModel更新）
        /// </summary>
        private void UpdateStatus(string status)
        {
            if (_viewModel != null)
            {
                _viewModel.StatusMessage = status;
            }
        }

        /// <summary>
        /// 获取设备地址（从ViewModel）
        /// </summary>
        private string GetDeviceAddress()
        {
            return _viewModel?.DeviceAddress ?? "1";
        }

        #endregion

        #region 串口操作

        // ❌ 已删除：btn_RefreshPorts_Click - 已被 RefreshPortsCommand 替代
        // ❌ 已删除：btn_OpenSerial_Click - 已被 OpenSerialCommand 和 CloseSerialCommand 替代
        //    原逻辑已集成到 SerialPortManager 和 OnSerialConnectionChanged 中

        #endregion

        #region 读取操作

        // ❌ 已删除：btn_ReadSerialNumber_Click - 已被 ReadSerialNumberCommand 替代
        // ❌ 已删除：btn_ReadStackInfo_Click - 已被 ReadStackInfoCommand 替代
        // ❌ 已删除：btn_ReadConfig_Click - 已被 ReadConfigCommand 替代

        /// <summary>
        /// 发送读取配置命令
        /// </summary>
        private bool SendReadConfigCommand(byte deviceAddress, byte paramTypeHigh, byte paramTypeLow, byte subIndex = 0)
        {
            // V4.3: parameter ID (2 bytes) + sub-index (1 byte).
            // A sub-index of 0 requests every parameter in the selected group.
            byte[] data = new byte[] { paramTypeHigh, paramTypeLow, subIndex };
            byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_READ_CONFIG, data);
            return SendFrame(frame);
        }

        #endregion

        #region 系统控制

        // ❌ 已删除：btn_StartStack_Click - 已被 StartStackCommand 替代
        // ❌ 已删除：btn_StopStack_Click - 已被 StopStackCommand 替代

        #endregion

        #region 电堆信息轮询

        /// <summary>
        /// 轮询定时器Tick事件
        /// </summary>
        private void PollingTimer_Tick(object? sender, EventArgs e)
        {
            if (_bmsCache?.Waiting == true || _standbyPeriod?.IsBusy == true || _currentCalibration?.IsBusy == true || _bmsBusExchangeInProgress || _bmsReturnInFlight) return;
            try
            {
                // 检查串口是否打开（静默检查，不显示消息）
                if (!_isSerialOpen)
                    return;

                // 发送读取电堆信息命令
                if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte deviceAddress, showError: false))
                    return;

                // [轮询修复] 上一轮至今未收到 0x87: 固件对 0x07 恒有响应, 持续未命中
                // 说明请求未到达或被固件 BMS 轮询窗口吞掉, 必须显式反馈而非静默
                if (_awaitingStackInfo)
                {
                    _stackInfoMissCount++;
                    // >= 而非 ==: 与 RX 线程清零交错时防跳过触发 (交错最多丢一次自增)
                    if (_stackInfoMissCount >= StackInfoMissThreshold)
                    {
                        if (_diagDoneForConnection)
                        {
                            // 本连接已自动诊断过: 不重复弹窗 (探针回包会清零计数导致周期性
                            // 重新触发), 仅状态栏滚动提示; 改对地址恢复响应后自动归零
                            UpdateStatus($"电堆信息轮询仍无 0x87 响应 (连续{_stackInfoMissCount}次)");
                        }
                        else
                        {
                            _diagDoneForConnection = true;
                            HC.Growl.Warning($"电堆信息轮询已连续{StackInfoMissThreshold}次无响应，正在自动诊断原因…");
                            UpdateStatus($"电堆信息轮询连续{_stackInfoMissCount}次无 0x87 响应，启动链路诊断");
                            StartStackInfoLinkDiagnosis(deviceAddress);
                        }
                    }
                }
                else
                {
                    _stackInfoMissCount = 0;
                }

                // [审查修复 P1②] BMS 事务在途(响应等待/重试挂起)时本轮跳过:
                // 0x07 与 Modbus 请求相撞双帧俱毁; 跳过轮不置 _awaitingStackInfo
                // (上一轮在途未答的 miss 已在上方统计块累计)
                if (_bmsService?.IsBusy == true)
                    return;

                byte[] frame = BuildReadFrame(deviceAddress, ProtocolConstants.FUNC_READ_STACK_INFO);
                _awaitingStackInfo = true;
                if (!SendFrame(frame))
                {
                    // 发送失败(串口瞬态写失败/拔线)时回滚在途标志,
                    // 防两个轮询互相卡死到用户干预 (审查建议)
                    _awaitingStackInfo = false;
                }
            }
            catch
            {
                // 轮询过程中出错，不影响主程序，静默处理
            }
        }

        /// <summary>
        /// 开启自动刷新按钮点击事件
        /// </summary>
        private void btn_StartPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                if (_pollingTimer == null)
                    return;

                // 新轮询会话复位健康监测状态 (含升级路径停表未复位的兜底) [审查修复 B审查建议]
                _awaitingStackInfo = false;
                _stackInfoMissCount = 0;
                _diagDoneForConnection = false;

                // 获取选择的轮询间隔
                var selectedItem = cbx_PollingInterval.SelectedItem as ComboBoxItem;
                if (selectedItem != null && selectedItem.Tag != null)
                {
                    int interval = int.Parse(selectedItem.Tag.ToString()!);
                    _pollingTimer.Interval = TimeSpan.FromMilliseconds(interval);
                }

                // 启动定时器
                _pollingTimer.Start();

                // 更新按钮状态
                btn_StartPolling.IsEnabled = false;
                btn_StopPolling.IsEnabled = true;
                cbx_PollingInterval.IsEnabled = false;

                UpdateStatus("已开启电堆信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"开启自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止自动刷新按钮点击事件
        /// </summary>
        private void btn_StopPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pollingTimer == null)
                    return;

                // 停止定时器
                _pollingTimer.Stop();

                // 更新按钮状态
                btn_StartPolling.IsEnabled = true;
                btn_StopPolling.IsEnabled = false;
                cbx_PollingInterval.IsEnabled = true;

                // 轮询健康监测状态复位, 防下次启动时把停止期间的静默误记为未命中
                _awaitingStackInfo = false;
                _stackInfoMissCount = 0;
                _diagDoneForConnection = false;

                UpdateStatus("已停止电堆信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"停止自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// [轮询修复] 轮询持续无响应时的链路诊断: 发一次广播(0x00)读电堆信息探针。
        /// 固件依据: fcu_protocol.c:527-530 接受广播地址, fcu_handle_read_stack_info 恒回
        /// 0x87 (地址回显 0x00)。探针有响应 → 链路正常, 是设备地址不匹配;
        /// 无响应 → 命令根本没到 FCU (接线/方向/供电, 或 FCU 侧 BMS 轮询窗口持续吞帧)。
        /// 注意: 广播探针假定总线上仅这一台 FCU, 多机共线会同时应答造成碰撞
        /// (产品使用模型为单 FCU, 与升级页/多机切换页的假定一致)。
        /// </summary>
        private void StartStackInfoLinkDiagnosis(byte deviceAddress)
        {
            if (deviceAddress == 0)
            {
                // 本身就以广播地址轮询, 无第二种地址可鉴别, 直接按链路不通提示
                ShowStackInfoDiagnosisResult(false);
                return;
            }

            _diagProbeFunc = 0;      /* 先复位 func 再置 pending: 与 RX 侧写序镜像, 防上一轮迟响应的覆盖竞态 */
            _diagProbePending = true;
            SendFrame(BuildReadFrame(0x00, ProtocolConstants.FUNC_READ_STACK_INFO));

            _diagProbeTimer?.Stop();
            _diagProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _diagProbeTimer.Tick += (s, e) =>
            {
                StopStackInfoLinkDiagnosis();
                ShowStackInfoDiagnosisResult(!_diagProbePending && _diagProbeFunc == 0x87);
            };
            _diagProbeTimer.Start();
        }

        private void StopStackInfoLinkDiagnosis()
        {
            _diagProbeTimer?.Stop();
            _diagProbeTimer = null;
        }

        /// <summary>诊断结论提示: 区分地址不匹配与链路不通, 给出对应处置</summary>
        private void ShowStackInfoDiagnosisResult(bool probeAnswered)
        {
            if (probeAnswered)
            {
                HC.Growl.Warning($"广播探测已获 FCU 响应: 链路正常, 但设备地址[{GetDeviceAddress()}]的轮询无响应——" +
                                 "FCU 实际地址与上位机设置不一致, 请核对后在连接区修改设备地址");
                UpdateStatus("轮询诊断: 链路正常, 疑似设备地址不匹配");
            }
            else
            {
                HC.Growl.Warning("广播探测无响应: 上位机命令未到达 FCU——请检查 485 接线/转换器方向/FCU 供电。" +
                                 "若连接时曾提示「BMS 总线接管未确认」, 也可能是 FCU 侧 BMS 轮询窗口持续吞帧, 请断开重新连接。" +
                                 "注意: 串口工具上每 2 秒出现的 01 03 00 80 00 19 开头帧是 FCU 自身轮询 BMS 的流量, 并非对本机命令的回复");
                UpdateStatus("轮询诊断: 广播无响应, 链路不通或 FCU 未上电");
            }
        }

        #endregion

        #region 读取参数按钮

        /// <summary>
        /// 读取压力参数
        /// </summary>
        private void btn_ReadPressureParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_PRESSURE >> 8), (byte)(ProtocolConstants.PARAM_TYPE_PRESSURE & 0xFF));
                UpdateStatus("已发送读取压力参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取压力参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取阀门参数
        /// </summary>
        /// <summary>
        /// 读取阀门页全部FCU参数（0x0A+0x0201 阀门排水时序、0x0A+0x0202 扩展时序、0x0A+0x0601 工作流程），回填到本页输入框。
        /// 相邻帧间隔100ms，避免RS485半双工总线与固件应答冲突（同 btn_ReadLoadParams_Click 既有模式）。
        /// </summary>
        private void btn_ReadValvePageParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_VALVE >> 8), (byte)(ProtocolConstants.PARAM_TYPE_VALVE & 0xFF));
                System.Threading.Thread.Sleep(100);

                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_VALVE_TIMING >> 8), (byte)(ProtocolConstants.PARAM_TYPE_VALVE_TIMING & 0xFF));
                System.Threading.Thread.Sleep(100);

                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_FLOW >> 8), (byte)(ProtocolConstants.PARAM_TYPE_FLOW & 0xFF));

                UpdateStatus("已发送读取阀门/扩展时序/工作流程参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取阀门页参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取DCDC参数
        /// </summary>
        private void btn_ReadDCDCParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_DCDC >> 8), (byte)(ProtocolConstants.PARAM_TYPE_DCDC & 0xFF));
                UpdateStatus("已发送读取DCDC参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取DCDC参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取风扇温度参数
        /// </summary>
        private void btn_ReadFanTempParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_FAN_TEMP >> 8), (byte)(ProtocolConstants.PARAM_TYPE_FAN_TEMP & 0xFF));
                UpdateStatus("已发送读取风扇温度参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取风扇温度参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取通用参数
        /// </summary>
        private void btn_ReadGeneralParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_GENERAL >> 8), (byte)(ProtocolConstants.PARAM_TYPE_GENERAL & 0xFF));
                UpdateStatus("已发送读取通用参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取通用参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取拉载参数
        /// </summary>
        private void btn_ReadLoadParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                
                // 读取拉载时序参数 (0x0501)
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE >> 8), (byte)(ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE & 0xFF));
                System.Threading.Thread.Sleep(100);

                // 读取拉载控制参数 (0x0502)
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_LOAD_CONTROL >> 8), (byte)(ProtocolConstants.PARAM_TYPE_LOAD_CONTROL & 0xFF));
                System.Threading.Thread.Sleep(100);

                // 读取拉载启动参数 (0x0503)
                SendReadConfigCommand(deviceAddress, (byte)(ProtocolConstants.PARAM_TYPE_LOAD_STARTUP >> 8), (byte)(ProtocolConstants.PARAM_TYPE_LOAD_STARTUP & 0xFF));

                UpdateStatus("已发送读取整套电流参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取电流参数失败: {ex.Message}");
            }
        }

        #endregion

        #region 参数设置

        /// <summary>
        /// 整帧写入压力参数（0x02，18B 固化 Flash）。
        /// 未先「读取压力参数」时，界面值（含未展示的红/黄阈值）会整帧覆盖设备，需二次确认。
        /// </summary>
        private void btn_SetPressureParams_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return;
            }

            if (!_pressureParamsLoadedFromDevice)
            {
                var confirm = MessageBox.Show(
                    "尚未从设备读取压力参数，将以上位机当前界面值整帧覆盖设备全部 8 项并固化到 Flash（含未在界面展示的高压红/黄阈值）。\n\n建议先「读取压力参数」以保留设备当前值。确定继续写入？",
                    "压力参数写入确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            if (_viewModel?.WritePressureParamsCommand is { } writeCommand
                && writeCommand.CanExecute(null))
            {
                writeCommand.Execute(null);
            }
            else
            {
                HC.Growl.Warning("串口已断开或升级占用，压力参数未发送");
            }
        }

        private void btn_SetValveParams_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("阀门参数并固化到FCU");
        }

        private void btn_SetDCDCParams_Click(object sender, RoutedEventArgs e)
        {
            SetDCDCParamHelper("参数并固化到FCU");
        }

        private void btn_SetFanTempParams_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("风扇温度参数并固化到FCU");
        }

        /// <summary>
        /// 启用风扇手动占空比（0x0F，数据域 enable=1、duty=1~100）。
        /// 固件无状态门控（任意状态立即生效）且不写 Flash，仅本次上电运行期间有效。
        /// duty=0 会立即停转风扇，温度传感器异常时固件无兜底，予以拦截。
        /// </summary>
        private void btn_EnableFanManual_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseRange(tbx_FanManualDuty, "手动风扇占空比", 0, 100, "%", out ushort duty))
            {
                return;
            }

            if (duty == 0)
            {
                HC.Growl.Warning("手动占空比 0% 会立即停转风扇；温度传感器异常时固件无兜底。请使用 1% 以上，或改用「恢复自动温控」");
                return;
            }

            var confirm = MessageBox.Show(
                $"确定将风扇切到手动模式，占空比 {duty}%？\n\n任意状态立即生效；仅本次上电有效（不写Flash），FCU 重启或超温保护后自动恢复温控。",
                "风扇手动控制确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            SendFanManualControl(true, (byte)duty);
        }

        /// <summary>
        /// 退出手动控制并恢复PEM温度闭环（0x0F，enable=0、duty=0）。
        /// </summary>
        private void btn_RestoreFanAutomatic_Click(object sender, RoutedEventArgs e)
        {
            SendFanManualControl(false, 0);
        }

        private void SendFanManualControl(bool enable, byte duty)
        {
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return;
            }

            if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte deviceAddress))
            {
                return;
            }

            byte wireDuty = enable ? duty : (byte)0;
            _pendingFanManualEnable = enable;
            _pendingFanManualDuty = wireDuty;

            if (!SendFrame(ProtocolHandler.BuildRequestFrame(deviceAddress,
                    ProtocolConstants.FUNC_SET_FAN_MANUAL,
                    new[] { enable ? (byte)1 : (byte)0, wireDuty })))
            {
                _pendingFanManualEnable = null;
                tbk_FanManualMode.Text = "发送失败（串口未打开或升级占用）";
                tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                return;
            }
            StartFanManualResponseTimeout();

            tbk_FanManualMode.Text = enable
                ? $"等待FCU确认：手动 {wireDuty}%"
                : "等待FCU确认：恢复自动温控";
            tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
            UpdateStatus(enable
                ? $"已发送风扇手动控制命令：{wireDuty}%"
                : "已发送恢复风扇自动温控命令");
        }

        // ❌ 已删除：在线测试(0x0D)相关方法与6个按钮事件
        // 固件从未实现在线测试功能码, 且与 set_bms_poll(0x0D) 撞码 [上位机优化计划 D3]

        // ❌ 已删除：7个压力参数设置按钮事件处理方法
        // btn_SetHighPressureMin_Click, btn_SetHighPressureRed_Click, 
        // btn_SetHighPressureYellow_Click, btn_SetLowPressureMin_Click,
        // btn_SetLowPressureMax_Click, btn_SetHighPressureRange_Click,
        // btn_SetLowPressureRange_Click
        // 已全部被对应的 Command 替代

        // ❌ 已删除：5个阀门单项写入按钮事件处理方法
        // btn_SetValveFirstOpen_Click, btn_SetValveSecondOpen_Click,
        // btn_SetValveNormalInterval_Click, btn_SetValveNormalOpen_Click,
        // btn_SetValveShutdownOpen_Click
        // XAML 中已无对应按钮（deaff46 改版移除）；整组写入统一走 btn_SetValveParams_Click

        /// <summary>
        /// 阀门参数设置辅助方法（0x03 整帧写入，数据取 BuildValveParamsData）
        /// </summary>
        private void SetValveParamHelper(string paramName)
        {
            SetParameter(paramName, ProtocolConstants.FUNC_SET_VALVE_PARAMS, BuildValveParamsData);
        }

        /// <summary>
        /// 写入扩展时序（0x03+0x0202，固件 codex/configurable-valve-timing 分支）。
        /// 固件仅电堆 OFF 态接受（否则 0x82 回 0x03，自动启动竞争下可直接重试）；
        /// 九项须非 0 且排水领启 &lt; 氢阀领启（否则 0x02）。
        /// 0x82 无参数 ID，写入后登记 pending 供回落分支归属（见 HandleLoadManualResponse）。
        /// </summary>
        private void btn_SetValveTimingParams_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingValveTimingSet.HasValue &&
                (DateTime.Now - _pendingValveTimingSet.Value).TotalSeconds <= ValveTimingResponseTimeoutSeconds)
            {
                HC.Growl.Warning("上一条扩展时序指令尚在等待响应，请稍候");
                return;
            }

            if (_pendingLoadManualCmd != null)
            {
                HC.Growl.Warning("拉载手动指令在途，0x82 响应无法区分归属，请稍候再写入扩展时序");
                return;
            }

            if (!ConfirmValveTimingBudgetIfOverrun())
            {
                return;
            }

            bool sent = SetParameter("扩展时序参数(0x0202)", ProtocolConstants.FUNC_SET_VALVE_PARAMS,
                BuildValveTimingDataFromUi);
            if (sent)
            {
                _pendingValveTimingSet = DateTime.Now;
                RestartValveTimingResponseTimeout();
            }
            /* 发送/组包失败（false）：SetParameter 已弹错，不登记 pending、不启表（仿 SOC 失败回滚） */
        }

        /// <summary>
        /// 预算软校验：固件不校验时序总长（开机序列 20s 未完成撞 DCDC 门控 FAULT，
        /// 关机 30s 强制下电截断），写入前对超限配置弹确认。解析失败留待组包硬校验报错。
        /// </summary>
        private bool ConfirmValveTimingBudgetIfOverrun()
        {
            double[] startupDrain =
            {
                ReadTimingOrZero(tbx_StartupDrainLeadDelay), ReadTimingOrZero(tbx_StartupDrainFirstOpen),
                ReadTimingOrZero(tbx_StartupDrainFirstClose), ReadTimingOrZero(tbx_StartupDrainSecondOpen),
                ReadTimingOrZero(tbx_StartupDrainThirdInterval), ReadTimingOrZero(tbx_StartupDrainThirdOpen)
            };
            double[] startupH2 =
            {
                ReadTimingOrZero(tbx_StartupH2LeadDelay), ReadTimingOrZero(tbx_StartupH2FirstDelay),
                ReadTimingOrZero(tbx_StartupH2FirstOpen), ReadTimingOrZero(tbx_StartupH2ReopenDelay)
            };
            double drainTrack = startupDrain.Sum();
            double h2Track = startupH2.Sum();
            double shutdownDrain = ReadTimingOrZero(tbx_PurgeDrainHoldSec) + ReadTimingOrZero(tbx_ShutdownEmptySec);
            double shutdownH2 = ReadTimingOrZero(tbx_ShutdownH2Open1) + ReadTimingOrZero(tbx_ShutdownH2Close2) +
                                ReadTimingOrZero(tbx_ShutdownH2Open2);

            if (drainTrack <= 20.0 && h2Track <= 20.0 && shutdownDrain <= 30.0 && shutdownH2 <= 30.0)
            {
                return true;
            }

            var overrun = new List<string>();
            if (drainTrack > 20.0) overrun.Add($"开机排水轨 {drainTrack:0.00}s > 20s");
            if (h2Track > 20.0) overrun.Add($"开机氢阀轨 {h2Track:0.00}s > 20s");
            if (shutdownDrain > 30.0) overrun.Add($"关机排水 {shutdownDrain:0.00}s > 30s");
            if (shutdownH2 > 30.0) overrun.Add($"关机氢阀 {shutdownH2:0.00}s > 30s");

            var confirm = MessageBox.Show(
                $"当前时序配置超出固件安全时限：\n{string.Join("\n", overrun)}\n\n" +
                "固件不校验总时长：开机序列 20s 未完成将触发 DCDC 门控故障停机，关机超过 30s 会被强制下电截断。\n确定仍要写入吗？",
                "扩展时序超限确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return confirm == MessageBoxResult.Yes;
        }

        private static double ReadTimingOrZero(TextBox box)
        {
            return double.TryParse(box.Text?.Trim(), out double v) && v >= 0 ? v : 0;
        }

        /// <summary>
        /// 组包 0x03+0x0202（数据域 20B：参数ID 2B + 9×u16 大端 0.1s，整帧 27B）。
        /// 硬校验对齐固件 fcu_set_valve_timing：九项非 0（ParseValveTimingField 保证 ≥1）、
        /// 上位机上限 60.0s（600）、startup_drain_delay &lt; startup_h2_delay（违反固件回 0x02）。
        /// </summary>
        private byte[] BuildValveTimingDataFromUi()
        {
            ushort pause1 = ParseValveTimingField(tbx_StartupDrainFirstClose, "吹扫停止①(start_pause1)");
            ushort pause2 = ParseValveTimingField(tbx_StartupDrainThirdInterval, "吹扫停止②(start_pause2)");
            ushort third = ParseValveTimingField(tbx_StartupDrainThirdOpen, "电堆吹扫③(start_third_duration)");
            ushort h2Close1 = ParseValveTimingField(tbx_StartupH2FirstDelay, "氢阀初始关闭(h2_start_close1)");
            ushort h2Open = ParseValveTimingField(tbx_StartupH2FirstOpen, "氢阀打开①(h2_start_open)");
            ushort h2Close2 = ParseValveTimingField(tbx_StartupH2ReopenDelay, "氢阀再开前关闭(h2_start_close2)");
            ushort shutdownEmpty = ParseValveTimingField(tbx_ShutdownEmptySec, "关机排空保持(shutdown_empty)");
            ushort drainDelay = ParseValveTimingField(tbx_StartupDrainLeadDelay, "排水领启延时(startup_drain_delay)");
            ushort h2Delay = ParseValveTimingField(tbx_StartupH2LeadDelay, "氢阀领启延时(startup_h2_delay)");

            if (drainDelay >= h2Delay)
            {
                tbx_StartupDrainLeadDelay.Focus();
                tbx_StartupDrainLeadDelay.SelectAll();
                throw new ArgumentException(
                    $"排水领启延时({drainDelay / 10.0:F1}s)必须严格小于氢阀领启延时({h2Delay / 10.0:F1}s)，固件会拒绝整帧");
            }

            ushort[] values = { pause1, pause2, third, h2Close1, h2Open, h2Close2, shutdownEmpty, drainDelay, h2Delay };
            byte[] data = new byte[20];
            data[0] = (byte)(ProtocolConstants.PARAM_TYPE_VALVE_TIMING >> 8);
            data[1] = (byte)(ProtocolConstants.PARAM_TYPE_VALVE_TIMING & 0xFF);
            for (int i = 0; i < values.Length; i++)
            {
                data[2 + i * 2] = (byte)(values[i] >> 8);
                data[3 + i * 2] = (byte)(values[i] & 0xFF);
            }
            return data;
        }

        /// <summary>
        /// 解析扩展时序字段（秒→0.1s）。0x0202 九项上位机上限收紧为 60.0s（固件 u16 允许更宽，
        /// 预算警示用例可达所需），文案直接按 60.0s 口径提示，避免先报固件宽口径引起误导。
        /// </summary>
        private static ushort ParseValveTimingField(TextBox box, string fieldName)
        {
            if (!double.TryParse(box.Text?.Trim(), out double seconds))
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException($"{fieldName}「{box.Text}」不是合法数值（秒）");
            }

            double rounded = Math.Round(seconds * 10.0, MidpointRounding.AwayFromZero);
            if (!(rounded >= 1.0 && rounded <= 600.0))
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException($"{fieldName}超出范围：{seconds}s，允许 0.1～60.0s");
            }
            return (ushort)rounded;
        }

        /// <summary>
        /// 0x8A+0x0202 回填九框（÷10 转秒）并重算排氢关机总时长。
        /// </summary>
        private void UpdateValveTimingParams(ValveTimingParams data)
        {
            tbx_StartupDrainFirstClose.Text = (data.StartPause1 / 10.0).ToString("F2");
            tbx_StartupDrainThirdInterval.Text = (data.StartPause2 / 10.0).ToString("F2");
            tbx_StartupDrainThirdOpen.Text = (data.StartThirdDuration / 10.0).ToString("F2");
            tbx_StartupH2FirstDelay.Text = (data.H2StartClose1 / 10.0).ToString("F2");
            tbx_StartupH2FirstOpen.Text = (data.H2StartOpen / 10.0).ToString("F2");
            tbx_StartupH2ReopenDelay.Text = (data.H2StartClose2 / 10.0).ToString("F2");
            tbx_ShutdownEmptySec.Text = (data.ShutdownEmpty / 10.0).ToString("F2");
            tbx_StartupDrainLeadDelay.Text = (data.StartupDrainDelay / 10.0).ToString("F2");
            tbx_StartupH2LeadDelay.Text = (data.StartupH2Delay / 10.0).ToString("F2");
            UpdateShutdownTotal();
        }

        private bool _updatingShutdownTotal;

        /// <summary>
        /// 排氢关机总时长 = 关机排水保持(0x0E) + 关机排空保持(0x0202 shutdown_empty)。
        /// 公式单一实现（0x0202 回填、0x0601 回填、输入联动三处共用）；解析失败保持原值。
        /// </summary>
        private void UpdateShutdownTotal()
        {
            if (_updatingShutdownTotal ||
                !double.TryParse(tbx_PurgeDrainHoldSec?.Text, out double hold) ||
                !double.TryParse(tbx_ShutdownEmptySec?.Text, out double empty))
            {
                return;
            }
            _updatingShutdownTotal = true;
            try
            {
                tbx_ShutdownTotal.Text = (hold + empty).ToString("F2");
            }
            finally
            {
                _updatingShutdownTotal = false;
            }
        }

        /// <summary>0x0202 写入结果专属文案（0x82 回落分支按 pending 归属调用）。</summary>
        private void ShowValveTimingSetResult(byte result)
        {
            switch (result)
            {
                case ProtocolConstants.ERR_SUCCESS:
                    UpdateStatus("扩展时序(0x0202)参数设置成功");
                    HC.Growl.Success("扩展时序(0x0202)写入成功（已固化到FCU），可点击「读取FCU参数」核对回填");
                    break;
                case ProtocolConstants.ERR_DEVICE_BUSY:
                    UpdateStatus("扩展时序(0x0202)写入被拒：设备忙");
                    HC.Growl.Error("扩展时序(0x0202)写入被拒（0x03 设备忙）：需停机到关机状态后写入；电堆可能被自动启动抢占，可直接重试");
                    break;
                case ProtocolConstants.ERR_SAVE_FAILED:
                    UpdateStatus("扩展时序(0x0202)写入失败：Flash 保存失败");
                    HC.Growl.Error("扩展时序(0x0202)写入失败（0x09）：固件已回滚内存值；请重启 FCU 后读取全部参数核对，若读回默认值则参数页已损坏需重新配置");
                    break;
                default:
                    UpdateStatus($"扩展时序(0x0202)设置失败: {ProtocolConstants.GetErrorCodeDescription(result)}");
                    HC.Growl.Error($"扩展时序(0x0202)设置失败：{ProtocolConstants.GetErrorCodeDescription(result)}");
                    break;
            }
        }

        /// <summary>
        /// 设置DCDC最低工作电压
        /// </summary>
        private void btn_SetDCDCMinVoltage_Click(object sender, RoutedEventArgs e)
        {
            SetDCDCParamHelper("最低工作电压");
        }

        /// <summary>
        /// 设置DCDC放电截至电压
        /// </summary>
        private void btn_SetDCDCStopVoltage_Click(object sender, RoutedEventArgs e)
        {
            SetDCDCParamHelper("放电截至电压");
        }

        /// <summary>
        /// DCDC参数设置辅助方法
        /// </summary>
        private void SetDCDCParamHelper(string paramName)
        {
            SetParameter($"DCDC{paramName}", ProtocolConstants.FUNC_SET_DCDC_PARAMS, BuildDCDCParamsData);
        }

        /// <summary>
        /// 设置低速占空比
        /// </summary>
        private void btn_SetFanLowDuty_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("低速占空比");
        }

        /// <summary>
        /// 设置温度下限
        /// </summary>
        private void btn_SetTempMin_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("温度下限");
        }

        /// <summary>
        /// 设置温度上限
        /// </summary>
        private void btn_SetTempMax_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("温度上限");
        }

        /// <summary>
        /// 设置全速运行时长
        /// </summary>
        private void btn_SetFanFullSpeedTime_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("全速运行时长");
        }

        /// <summary>
        /// 设置低温检测时长
        /// </summary>
        private void btn_SetLowTempCheckTime_Click(object sender, RoutedEventArgs e)
        {
            SetFanTempParamHelper("低温检测时长");
        }

        /// <summary>
        /// 风扇温度参数设置辅助方法
        /// </summary>
        private void SetFanTempParamHelper(string paramName)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 风扇温度数据域13B：参数ID + 5个原有字段 + fan_min_rpm。
                // fan_min_rpm=0 表示不启用堵转检测；非0时固件(协议V5.1)在运行态
                // 占空比>30%且连续3个2s窗口转速低于该值时上报0x0004风扇异常(WARNING,不停机)。
                // 阈值须按0x87实测转速标定(测点取略高于30%占空比工况)，勿用数据手册值。
                // 温度上下限固件为 uint16_t 无符号（fcu_protocol.h temp_lower/upper_limit），
                // 且下限为 0 会被整帧拒收，这里按 0.1℃ 无符号解析并禁 0。
                if (!TryParseRange(tbx_FanLowDuty, "低速占空比", 0, 100, "%", out ushort lowDutyValue) ||
                    !TryParseScaledRange(tbx_TempMin, "温度下限", 10m, 1, 1250, "℃", out ushort tempMin) ||
                    !TryParseScaledRange(tbx_TempMax, "温度上限", 10m, 1, 1250, "℃", out ushort tempMax) ||
                    !TryParseRange(tbx_FanFullSpeedTime, "全速运行时长", 1, ushort.MaxValue, "0.1s", out ushort fullSpeedTime) ||
                    !TryParseRange(tbx_LowTempCheckTime, "低温检测时长", 1, ushort.MaxValue, "0.1s", out ushort lowTempCheckTime) ||
                    !TryParseRange(tbx_FanMinRpm, "风扇最低转速", 0, ushort.MaxValue, "RPM", out ushort fanMinRpm))
                {
                    return;
                }

                if (tempMin >= tempMax)
                {
                    HC.Growl.Warning($"温度下限({tempMin / 10m:0.0}℃)必须小于温度上限({tempMax / 10m:0.0}℃)，固件会拒绝整帧");
                    return;
                }

                byte lowDuty = (byte)lowDutyValue;
                byte[] data = BuildFanTempParamsData(lowDuty, tempMin, tempMax,
                    fullSpeedTime, lowTempCheckTime, fanMinRpm);

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_FAN_TEMP_PARAMS, data);
                SendFrame(frame);
                UpdateStatus($"已发送设置{paramName}命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按《FCU参数新增与建议清单》生成风扇温度13字节SET数据域（大端）。
        /// 温度上下限为无符号 uint16（0.1℃），与固件 fcu_protocol.h 字段类型一致。
        /// </summary>
        private static byte[] BuildFanTempParamsData(byte lowDuty, ushort tempMin,
            ushort tempMax, ushort fullSpeedTime, ushort lowTempCheckTime, ushort fanMinRpm)
        {
            byte[] data = new byte[ProtocolConstants.FAN_TEMP_PARAMS_DATA_LENGTH];
            data[0] = 0x04; data[1] = 0x01;
            data[2] = lowDuty;
            data[3] = (byte)(tempMin >> 8); data[4] = (byte)(tempMin & 0xFF);
            data[5] = (byte)(tempMax >> 8); data[6] = (byte)(tempMax & 0xFF);
            data[7] = (byte)(fullSpeedTime >> 8); data[8] = (byte)(fullSpeedTime & 0xFF);
            data[9] = (byte)(lowTempCheckTime >> 8); data[10] = (byte)(lowTempCheckTime & 0xFF);
            data[11] = (byte)(fanMinRpm >> 8); data[12] = (byte)(fanMinRpm & 0xFF);
            return data;
        }

        /// <summary>
        /// 设置拉载时序参数
        /// </summary>
        private void btn_SetLoadSequenceParams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                /* 0x0504 在途期间互斥其他 0x04 类发送 (0x82 无子命令回显, 防误路由) */
                if (_pendingLoadManualCmd != null)
                {
                    HC.Growl.Warning($"拉载指令[{_pendingLoadManualCmd}]仍在等待FCU响应，暂不发送时序参数");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                TextBox[] currentBoxes = {
                    tbx_Seq1_Current, tbx_Seq2_Current, tbx_Seq3_Current,
                    tbx_Seq4_Current, tbx_Seq5_Current, tbx_Seq6_Current,
                    tbx_Seq7_Current, tbx_Seq8_Current, tbx_Seq9_Current,
                    tbx_Seq10_Current, tbx_Seq11_Current, tbx_Seq12_Current,
                    tbx_Seq13_Current, tbx_Seq14_Current, tbx_Seq15_Current
                };
                TextBox[] timeBoxes = {
                    tbx_Seq1_Time, tbx_Seq2_Time, tbx_Seq3_Time,
                    tbx_Seq4_Time, tbx_Seq5_Time, tbx_Seq6_Time,
                    tbx_Seq7_Time, tbx_Seq8_Time, tbx_Seq9_Time,
                    tbx_Seq10_Time, tbx_Seq11_Time, tbx_Seq12_Time,
                    tbx_Seq13_Time, tbx_Seq14_Time, tbx_Seq15_Time
                };

                // 下位机对时序表整帧校验，任一步非法则整张表不生效，
                // 因此发送前先逐步校验并明确指出出错的时序编号；
                // 第 15 条留空 → count=14（对齐固件 V1.2 默认 14 条）
                if (!TryParseLoadSequences(currentBoxes, timeBoxes,
                        out int seqCount, out ushort[] currents, out ushort[] times))
                {
                    return;
                }

                // count 个时序，每个时序 4 字节（电流 2 字节 + 时间 2 字节）
                byte[] data = new byte[2 + 1 + seqCount * 4];
                data[0] = 0x05; // 参数类型ID 高字节
                data[1] = 0x01; // 参数类型ID 低字节
                data[2] = (byte)seqCount;   // 时序总数（14 或 15）

                for (int i = 0; i < seqCount; i++)
                {
                    data[3 + i * 4] = (byte)(currents[i] >> 8);
                    data[4 + i * 4] = (byte)(currents[i] & 0xFF);
                    data[5 + i * 4] = (byte)(times[i] >> 8);
                    data[6 + i * 4] = (byte)(times[i] & 0xFF);
                }

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);
                SendFrame(frame);
                UpdateStatus($"已发送设置拉载时序参数命令（{seqCount} 条）");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置拉载时序参数失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 设置峰值电流
        /// </summary>
        private void btn_SetLoadPeakCurrent_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("峰值电流");
        }

        /// <summary>
        /// 一次写入唯一一套电流运行参数。
        /// </summary>
        private void btn_SetLoadControlParams_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("运行参数");
        }

        /// <summary>
        /// 设置锁定系数
        /// </summary>
        private void btn_SetLoadLockFactor_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("锁定系数");
        }

        /// <summary>
        /// 设置锁定标志
        /// </summary>
        private void btn_SetLoadLockFlag_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("锁定标志");
        }

        /// <summary>
        /// 设置拉载最低电压
        /// </summary>
        private void btn_SetLoadMinVoltage_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("最低电压");
        }

        /// <summary>
        /// 设置电流步长
        /// </summary>
        private void btn_SetLoadCurrentStep_Click(object sender, RoutedEventArgs e)
        {
            SetLoadControlParamHelper("电流步长");
        }

        /// <summary>
        /// 拉载控制参数设置辅助方法
        /// </summary>
        private void SetLoadControlParamHelper(string paramName)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                /* 0x0504 在途期间互斥其他 0x04 类发送 (0x82 无子命令回显, 防误路由) */
                if (_pendingLoadManualCmd != null)
                {
                    HC.Growl.Warning($"拉载指令[{_pendingLoadManualCmd}]仍在等待FCU响应，暂不发送{paramName}");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                /* lock_coefficient 为固件保留字段(最大功率锁定不再乘系数, fcu_protocol.h 注释):
                 * 控件已置灰, 组包按回读缓存透传(初值 900=固件默认), 不再取控件值参与校验 */
                ushort lockFactor = _lastLockFactor0P001;

                // 下位机 fcu_protocol_set_load_control() 的校验范围，发送前先本地拦截
                if (!TryParseScaledRange(tbx_LoadPeakCurrent, "峰值电流",
                        10m, 1, LoadMaxCurrent0P1A, "A", out ushort peakCurrent) ||
                    !TryParseScaledRange(tbx_LoadMinVoltage, "最低电压",
                        10m, 1, ushort.MaxValue, "V", out ushort minVoltage) ||
                    !TryParseScaledRange(tbx_LoadCurrentStep, "电流步长",
                        10m, 1, ushort.MaxValue, "A", out ushort currentStep))
                {
                    return;
                }

                byte lockFlag = (byte)(chk_LoadLockFlag.IsChecked == true ? 1 : 0);

                byte[] data = new byte[11];
                data[0] = 0x05; data[1] = 0x02;
                data[2] = (byte)(peakCurrent >> 8); data[3] = (byte)(peakCurrent & 0xFF);
                data[4] = (byte)(lockFactor >> 8); data[5] = (byte)(lockFactor & 0xFF);
                data[6] = lockFlag;
                data[7] = (byte)(minVoltage >> 8); data[8] = (byte)(minVoltage & 0xFF);
                data[9] = (byte)(currentStep >> 8); data[10] = (byte)(currentStep & 0xFF);

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);
                SendFrame(frame);
                UpdateStatus($"已发送设置拉载{paramName}命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置首次拉载次数
        /// </summary>
        private void btn_SetLoadFirstCount_Click(object sender, RoutedEventArgs e)
        {
            SetLoadStartParamHelper("首次拉载次数");
        }

        /// <summary>
        /// 设置首次拉载间隔
        /// </summary>
        private void btn_SetLoadFirstInterval_Click(object sender, RoutedEventArgs e)
        {
            SetLoadStartParamHelper("首次拉载间隔");
        }

        /// <summary>
        /// 设置正常拉载间隔
        /// </summary>
        private void btn_SetLoadNormalInterval_Click(object sender, RoutedEventArgs e)
        {
            SetLoadStartParamHelper("正常拉载间隔");
        }

        /// <summary>
        /// 一次写入唯一一套电流执行周期参数。
        /// </summary>
        private void btn_SetLoadStartParams_Click(object sender, RoutedEventArgs e)
        {
            SetLoadStartParamHelper("执行周期参数");
        }

        /// <summary>
        /// 拉载启动参数设置辅助方法
        /// </summary>
        private void SetLoadStartParamHelper(string paramName)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                /* 0x0504 在途期间互斥其他 0x04 类发送 (0x82 无子命令回显, 防误路由) */
                if (_pendingLoadManualCmd != null)
                {
                    HC.Growl.Warning($"拉载指令[{_pendingLoadManualCmd}]仍在等待FCU响应，暂不发送{paramName}");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 下位机 fcu_control_set_load_startup_params() 的校验范围，发送前先本地拦截
                if (!TryParseRange(tbx_LoadFirstCount, "首次拉载次数",
                        1, 255, "次", out ushort firstCountValue) ||
                    !TryParseRange(tbx_LoadFirstInterval, "首次拉载间隔",
                        1, ushort.MaxValue, "分钟", out ushort firstInterval) ||
                    !TryParseRange(tbx_LoadNormalInterval, "正常拉载间隔",
                        1, ushort.MaxValue, "分钟", out ushort normalInterval))
                {
                    return;
                }

                // 下位机要求 normal_interval > first_interval，否则整帧被拒
                if (normalInterval <= firstInterval)
                {
                    HC.Growl.Warning($"正常拉载间隔({normalInterval}分钟)必须大于首次拉载间隔({firstInterval}分钟)");
                    return;
                }

                byte firstCount = (byte)firstCountValue;

                byte[] data = new byte[7];
                data[0] = 0x05; data[1] = 0x03;
                data[2] = firstCount;
                data[3] = (byte)(firstInterval >> 8); data[4] = (byte)(firstInterval & 0xFF);
                data[5] = (byte)(normalInterval >> 8); data[6] = (byte)(normalInterval & 0xFF);

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);
                SendFrame(frame);
                UpdateStatus($"已发送设置{paramName}命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 修改设备地址按钮点击事件
        /// </summary>
        private void btn_SetDeviceAddress_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 验证新设备地址
                if (!byte.TryParse(tbx_NewDeviceAddress.Text, out byte newAddress))
                {
                    MessageBox.Show("请输入有效的设备地址（1-247）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (newAddress < 1 || newAddress > 247)
                {
                    MessageBox.Show("设备地址必须在1-247范围内", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 确认操作
                var result = MessageBox.Show(
                    $"确定要将设备地址修改为 {newAddress} 吗？\n\n注意：新地址将在设备重启后生效！",
                    "确认修改",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // 构建功能码0x0C修改设备地址请求帧
                byte currentAddress = byte.Parse(GetDeviceAddress());
                byte[] dataField = new byte[] { newAddress };
                byte[] frame = ProtocolHandler.BuildRequestFrame(currentAddress, ProtocolConstants.FUNC_SET_DEVICE_ADDRESS, dataField);

                SendFrame(frame);
                UpdateStatus($"已发送修改设备地址指令 (新地址: {newAddress})");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"修改设备地址失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"修改设备地址失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置扫描周期
        /// </summary>
        private void btn_SetScanPeriod_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 下位机 fcu_handle_set_general() 拒绝 scan_period == 0
                if (!TryParseRange(tbx_ScanPeriod, "扫描周期",
                        1, ushort.MaxValue, "0.1s", out ushort scanPeriod))
                {
                    return;
                }

                byte[] data = new byte[4];
                data[0] = 0x09; data[1] = 0x01;
                data[2] = (byte)(scanPeriod >> 8); data[3] = (byte)(scanPeriod & 0xFF);

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_GENERAL_PARAMS, data);
                SendFrame(frame);
                UpdateStatus("已发送设置扫描周期命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"设置失败: {ex.Message}");
            }
        }

        #region 参数前端校验

        /// <summary>
        /// 拉载电流上限（0.1A），对应下位机 FCU_DCDC_MAX_CURRENT_0P1A = 500（50.0A）
        /// </summary>
        private const ushort LoadMaxCurrent0P1A = 500;

        /// <summary>
        /// 解析文本框并做范围校验，非法时抛 ArgumentException。
        /// 供 SetParameter() 的 dataBuilder 委托使用——那里已有统一的 try-catch 展示错误信息。
        /// </summary>
        /// <param name="box">待校验的文本框</param>
        /// <param name="fieldName">字段显示名</param>
        /// <param name="min">允许的最小值（含）</param>
        /// <param name="max">允许的最大值（含）</param>
        /// <param name="unit">单位说明</param>
        private static ushort ParseRequiredField(TextBox box, string fieldName,
            int min, int max, string unit)
        {
            if (!ushort.TryParse(box.Text?.Trim(), out ushort value))
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException($"{fieldName}「{box.Text}」不是合法整数");
            }

            if (value < min || value > max)
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException(
                    $"{fieldName}超出范围：{value}，允许 {min}~{max}（{unit}）");
            }

            return value;
        }

        /// <summary>
        /// 解析秒值输入框并换算为协议 0.1s 单位（固件 u16：1~65535 即 0.1~6553.5s）。
        /// 阀门页时序输入框为秒值（如 1.20），0x03/0x0E 数据域为 0.1s 整数（如 12）。
        /// 失败时聚焦控件并抛出带字段名的异常（由 SetParameter 统一展示）。
        /// 注意：必须先对 double 做范围检查再强转 ushort——
        /// 负数或 >6553.5s 的强转会按 mod 65536 回绕成看似合法的错误值（如 "-1"→65526），
        /// 绕过非零校验后被固件接受并固化 Flash。
        /// </summary>
        private static ushort ParseSecondsTo0P1(TextBox box, string fieldName)
        {
            if (!double.TryParse(box.Text?.Trim(), out double seconds))
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException($"{fieldName}「{box.Text}」不是合法数值（秒）");
            }

            double rounded = Math.Round(seconds * 10.0, MidpointRounding.AwayFromZero);
            /* 取反写法使 NaN 一并被拦截（NaN 与任何数比较均为 false） */
            if (!(rounded >= 1.0 && rounded <= 65535.0))
            {
                box.Focus();
                box.SelectAll();
                throw new ArgumentException($"{fieldName}超出范围：{seconds}s，允许 0.1~6553.5s");
            }

            return (ushort)rounded;
        }

        /// <summary>
        /// 解析文本框中的 ushort 并做范围校验，失败时给出明确提示并聚焦到出错控件
        /// </summary>
        /// <param name="box">待校验的文本框</param>
        /// <param name="fieldName">字段显示名，用于提示信息</param>
        /// <param name="min">允许的最小值（含）</param>
        /// <param name="max">允许的最大值（含）</param>
        /// <param name="unit">单位说明，用于提示信息</param>
        /// <param name="value">解析成功后的值</param>
        /// <returns>校验通过返回 true</returns>
        private static bool TryParseRange(TextBox box, string fieldName,
            int min, int max, string unit, out ushort value)
        {
            value = 0;

            if (!ushort.TryParse(box.Text?.Trim(), out ushort parsed))
            {
                HC.Growl.Warning($"{fieldName}「{box.Text}」不是合法整数");
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (parsed < min || parsed > max)
            {
                HC.Growl.Warning($"{fieldName}超出范围：{parsed}，允许 {min}~{max}（{unit}）");
                box.Focus();
                box.SelectAll();
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// 按界面工程单位解析小数，并转换为下位机协议整数。
        /// </summary>
        private static bool TryParseScaledRange(TextBox box, string fieldName,
            decimal scale, int minRaw, int maxRaw, string unit, out ushort rawValue)
        {
            rawValue = 0;
            string text = box.Text?.Trim() ?? string.Empty;

            bool parsed = decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal displayValue)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out displayValue);
            if (!parsed)
            {
                HC.Growl.Warning($"{fieldName}「{text}」不是合法数值");
                box.Focus();
                box.SelectAll();
                return false;
            }

            decimal scaled = displayValue * scale;
            decimal rounded = decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
            if (scaled != rounded)
            {
                int decimalPlaces = scale == 1000m ? 3 : scale == 100m ? 2 : 1;
                HC.Growl.Warning($"{fieldName}最多支持 {decimalPlaces} 位小数");
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (rounded < minRaw || rounded > maxRaw)
            {
                decimal minDisplay = minRaw / scale;
                decimal maxDisplay = maxRaw / scale;
                HC.Growl.Warning($"{fieldName}超出范围：{displayValue}，允许 {minDisplay}~{maxDisplay}{unit}");
                box.Focus();
                box.SelectAll();
                return false;
            }

            rawValue = (ushort)rounded;
            return true;
        }

        /// <summary>
        /// 尾部"空行"判定：两格均空白，或两格均为 0（方案页未用行默认填 "0"；电流/时间合法下限均为 1，0 永远不是有效值）。
        /// 返回 null 表示两格状态不一致（一空一有值），交由逐行校验报错。
        /// </summary>
        private static bool? IsTailEmptyRow(string currentText, string timeText)
        {
            bool curBlank = string.IsNullOrWhiteSpace(currentText);
            bool timeBlank = string.IsNullOrWhiteSpace(timeText);
            if (curBlank && timeBlank) return true;
            if (curBlank != timeBlank) return null;

            bool curZero = decimal.TryParse(currentText?.Trim(), out var c) && c == 0m;
            bool timeZero = decimal.TryParse(timeText?.Trim(), out var t) && t == 0m;
            if (curZero && timeZero) return true;
            if (curZero != timeZero) return null;
            return false;
        }

        /// <summary>
        /// 校验并解析拉载时序（支持尾部空行）。下位机对时序表整帧校验（电流 1~500、时间 ≥1，条数 ≤64），
        /// 任一步非法会导致整张表都不生效，因此这里逐步校验并指明出错的时序编号。
        /// 规则：仅允许"尾部连续空行"（第 15 条留空/为 0 → 按 14 条发送，对齐固件 V1.2 默认 14 条时序表）；
        /// 中间空行（前一行有值、后一行也有值）拒绝——固件时序表不支持空洞。
        /// </summary>
        /// <param name="currentBoxes">时序电流文本框（15 个）</param>
        /// <param name="timeBoxes">时序时间文本框（15 个）</param>
        /// <param name="count">实际有效时序条数（尾部空行不计）</param>
        /// <param name="currents">解析出的电流数组（0.1A，仅前 count 个有效）</param>
        /// <param name="times">解析出的时间数组（0.01s，仅前 count 个有效）</param>
        /// <returns>全部校验通过返回 true</returns>
        private static bool TryParseLoadSequences(TextBox[] currentBoxes, TextBox[] timeBoxes,
            out int count, out ushort[] currents, out ushort[] times)
        {
            int total = currentBoxes.Length;
            currents = new ushort[total];
            times = new ushort[total];

            /* 从后往前找出最后一个有效行，其后为允许的尾部空行（含双 0 行） */
            int lastFilled = -1;
            for (int i = total - 1; i >= 0; i--)
            {
                var emptyState = IsTailEmptyRow(currentBoxes[i].Text, timeBoxes[i].Text);
                if (emptyState == false) { lastFilled = i; break; }
                if (emptyState == null) { lastFilled = i; break; }   /* 一空一有值: 视为有效行, 由逐行校验报错 */
            }
            count = lastFilled + 1;
            if (count == 0)
            {
                HC.Growl.Warning("时序表为空：请至少填写第 1 条时序");
                return false;
            }

            for (int i = 0; i <= lastFilled; i++)
            {
                /* 中间空行/双零行检测：有效区内电流与时间必须成对为非零有效值 */
                var emptyState = IsTailEmptyRow(currentBoxes[i].Text, timeBoxes[i].Text);
                if (emptyState == true)
                {
                    HC.Growl.Warning($"时序{i + 1} 电流与时间必须成对填写为有效值（仅允许末尾连续空行，固件时序表不支持空洞）");
                    return false;
                }
                if (emptyState == null)
                {
                    HC.Growl.Warning($"时序{i + 1} 电流与时间必须成对填写（两格需同时留空或同时为有效值）");
                    return false;
                }

                if (!TryParseScaledRange(currentBoxes[i], $"时序{i + 1}电流",
                        10m, 1, LoadMaxCurrent0P1A, "A", out currents[i]))
                {
                    return false;
                }

                if (!TryParseScaledRange(timeBoxes[i], $"时序{i + 1}时间",
                        100m, 1, ushort.MaxValue, "s", out times[i]))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region 拉载手动控制

        /// <summary>
        /// 发送拉载手动控制指令。
        /// 固件对 0x04 类请求统一回 0x82（fcu_protocol.c fcu_handle_set_load_params 响应尾），
        /// 0x82 不回显子命令码——用"最后发送的 0x0504 指令名"关联响应；在途期间互斥其他 0x04 类发送，消除误路由。
        /// </summary>
        /// <param name="commandCode">控制指令码</param>
        /// <param name="commandParams">指令参数（可为null）</param>
        /// <returns>是否已实际发出（false=未开串口/在途互斥/串口写失败(含 BL 升级模式拦截)/构造失败，调用方不应提示"已发送"）</returns>
        private bool SendLoadControlCommand(byte commandCode, byte[]? commandParams = null)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return false;
                }

                if (_pendingLoadManualCmd != null)
                {
                    HC.Growl.Warning($"拉载指令[{_pendingLoadManualCmd}]仍在等待FCU响应，请稍候再发下一条");
                    return false;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 构建数据域：参数类型ID(2) + 控制指令(1) + 指令参数(N)
                int paramLen = commandParams?.Length ?? 0;
                byte[] data = new byte[3 + paramLen];
                data[0] = 0x05; data[1] = 0x04;  // 参数类型ID 0x0504
                data[2] = commandCode;
                if (commandParams != null)
                    Array.Copy(commandParams, 0, data, 3, paramLen);

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);

                _pendingLoadManualCmd = GetLoadCommandDescription(commandCode);
                StartLoadManualResponseTimeout();
                if (!SendFrame(frame))
                {
                    /* 串口写失败: 立即回滚 pending, 不让互斥窗口空挂 5s (终审 R1) */
                    _pendingLoadManualCmd = null;
                    StopLoadManualResponseTimeout();
                    HC.Growl.Warning("拉载控制指令串口发送失败");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _pendingLoadManualCmd = null;
                StopLoadManualResponseTimeout();
                HC.Growl.Error($"拉载控制指令发送失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>拉载手动指令码 → 中文名（Growl/超时提示用）</summary>
        private static string GetLoadCommandDescription(byte commandCode) => commandCode switch
        {
            ProtocolConstants.LOAD_CMD_START => "启动拉载",
            ProtocolConstants.LOAD_CMD_PAUSE => "暂停拉载",
            ProtocolConstants.LOAD_CMD_RESUME => "继续拉载",
            ProtocolConstants.LOAD_CMD_STOP => "停止拉载",
            ProtocolConstants.LOAD_CMD_JUMP_SEQUENCE => "跳转时序",
            ProtocolConstants.LOAD_CMD_SET_CURRENT => "设置目标电流",
            ProtocolConstants.LOAD_CMD_STEP => "单步执行",
            ProtocolConstants.LOAD_CMD_RESET => "重置拉载",
            _ => $"未知指令(0x{commandCode:X2})"
        };

        /// <summary>
        /// 处理 0x0504 拉载手动控制的 0x82 响应（pending+超时模式）。
        /// 固件 0x0504 新语义（beforepined@51bf7e2 fcu_protocol.c:981-1030）：
        /// 仅 RUNNING 状态接受（STARTING 拒 0x03），八条命令返回真实错误码——弹窗级反馈呈现。
        /// 无在途 0x0504 指令时回落通用 HandleSetParamResponse（0x82 还承载 0x0501/0x0502/0x0503 的设置结果）。
        /// </summary>
        private void HandleLoadManualResponse(byte[] frame)
        {
            if (_pendingLoadManualCmd == null)
            {
                /* 0x0202 扩展时序的 0x82 归属（pending+2s 新鲜度，RX 线程快照/编组内清除，仿拉载模式）。
                 * 仅此回落分支消费；0x8E 直连 HandleSetParamResponse，不触碰本 pending。
                 * 2s 内其他 0x82 写的响应先到会冒用 pending（已披露残余风险）；超龄（计时器 Tick 未及触发的
                 * 竞态窗口）则清除 pending 并静默忽略本迟到响应——超时 Growl 已由计时器兜底。
                 * 结构注：0x82 泛化处理与 0x0202 pending 消费都以本回落分支为唯一入口。 */
                DateTime? valveTimingPending = _pendingValveTimingSet;
                if (valveTimingPending.HasValue)
                {
                    byte valveTimingResult = ResponseParser.ParseSetParamResponse(frame);
                    bool valveTimingFresh =
                        (DateTime.Now - valveTimingPending.Value).TotalSeconds <= ValveTimingResponseTimeoutSeconds;
                    MarshalRxToUi(() =>
                    {
                        _pendingValveTimingSet = null;
                        StopValveTimingResponseTimeout();
                        if (valveTimingFresh)
                        {
                            ShowValveTimingSetResult(valveTimingResult);
                        }
                    });
                    return;
                }
                HandleSetParamResponse(frame);
                return;
            }

            byte result = ResponseParser.ParseSetParamResponse(frame);
            /* 在接收线程快照 pending（改进自风扇模式）：防 5s 边界处 Tick 先清 pending、
             * 编组回调内读到 null 产生"拉载指令[]已执行"空名 Growl */
            string cmd = _pendingLoadManualCmd;
            MarshalRxToUi(() =>
            {
                StopLoadManualResponseTimeout();
                _pendingLoadManualCmd = null;

                if (result == ProtocolConstants.ERR_SUCCESS)
                {
                    UpdateStatus($"拉载指令[{cmd}]执行成功");
                    HC.Growl.Success($"拉载指令[{cmd}]已执行");
                    return;
                }

                string error = ProtocolConstants.GetErrorCodeDescription(result);
                /* 0x03 设备忙: 固件 load_manual 状态门为仅 RUNNING, STARTING 等状态均拒 0x03 */
                if (result == ProtocolConstants.ERR_DEVICE_BUSY)
                    error += "（电堆需处于运行状态）";
                UpdateStatus($"拉载指令[{cmd}]失败: {error}");
                HC.Growl.Error($"拉载指令[{cmd}]失败：{error}");
            });
        }

        /// <summary>
        /// 启动拉载
        /// </summary>
        private void btn_LoadStart_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_START))
                UpdateStatus("已发送启动拉载指令");
        }

        /// <summary>
        /// 暂停拉载
        /// </summary>
        private void btn_LoadPause_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_PAUSE))
                UpdateStatus("已发送暂停拉载指令");
        }

        /// <summary>
        /// 继续拉载
        /// </summary>
        private void btn_LoadResume_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_RESUME))
                UpdateStatus("已发送继续拉载指令");
        }

        /// <summary>
        /// 停止拉载
        /// </summary>
        private void btn_LoadStop_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_STOP))
                UpdateStatus("已发送停止拉载指令");
        }

        /// <summary>
        /// 跳转到时序
        /// </summary>
        private void btn_LoadJumpSequence_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte sequence = byte.Parse(tbx_JumpSequence.Text);
                if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_JUMP_SEQUENCE, new byte[] { sequence }))
                    UpdateStatus($"已发送跳转到时序{sequence}指令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"跳转到时序失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置目标电流
        /// </summary>
        private void btn_LoadSetCurrent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryParseScaledRange(tbx_TargetCurrent, "目标电流",
                        10m, 1, LoadMaxCurrent0P1A, "A", out ushort current))
                {
                    return;
                }
                byte[] paramData = new byte[] { (byte)(current >> 8), (byte)(current & 0xFF) };
                if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_SET_CURRENT, paramData))
                    UpdateStatus($"已发送设置目标电流{(current / 10.0):F1}A指令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"设置目标电流失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 单步执行
        /// </summary>
        private void btn_LoadStep_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_STEP))
                UpdateStatus("已发送单步执行指令");
        }

        /// <summary>
        /// 重置拉载
        /// </summary>
        private void btn_LoadReset_Click(object sender, RoutedEventArgs e)
        {
            if (SendLoadControlCommand(ProtocolConstants.LOAD_CMD_RESET))
                UpdateStatus("已发送重置拉载指令");
        }

        #endregion

        #endregion

        #region 通信协议辅助方法

        /// <summary>
        /// 构建通信帧
        /// </summary>
        private byte[] BuildFrame(byte address, byte functionCode, byte[] data)
        {
            int frameLength = ProtocolConstants.MIN_FRAME_LENGTH + data.Length; // 帧头(2) + 地址(1) + 功能码(1) + 长度(1) + 数据(N) + CRC(2)
            byte[] frame = new byte[frameLength];

            frame[0] = ProtocolConstants.FRAME_HEADER1; // 帧头
            frame[1] = ProtocolConstants.FRAME_HEADER2; // 帧头
            frame[2] = address; // 设备地址
            frame[3] = functionCode; // 功能码
            frame[4] = (byte)data.Length; // 数据长度

            Array.Copy(data, 0, frame, 5, data.Length);

            // 计算并添加CRC16校验
            ushort crc = CalculateCRC16(frame, frameLength - 2);
            frame[frameLength - 2] = (byte)(crc >> 8);
            frame[frameLength - 1] = (byte)(crc & 0xFF);

            return frame;
        }

        /// <summary>
        /// 构建读取帧（无数据）
        /// </summary>
        private byte[] BuildReadFrame(byte address, byte functionCode)
        {
            return BuildFrame(address, functionCode, new byte[0]);
        }

        /// <summary>
        /// 通用参数设置辅助方法 - 消除重复代码
        /// </summary>
        /// <param name="parameterName">参数名称（用于状态提示和错误信息）</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="dataBuilder">数据构建委托</param>
        /// <returns>是否设置成功</returns>
        private bool SetParameter(string parameterName, byte functionCode, Func<byte[]> dataBuilder)
        {
            try
            {
                if (!SerialPortHelper.CheckSerialPortOpen(_isSerialOpen))
                    return false;

                if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte deviceAddress))
                    return false;

                byte[] data = dataBuilder();
                byte[] frame = BuildFrame(deviceAddress, functionCode, data);
                if (!SendFrame(frame)) return false;
                UpdateStatus($"已发送设置{parameterName}命令");
                return true;
            }
            catch (Exception ex)
            {
                SerialPortHelper.ShowParameterSetError(parameterName, ex);
                return false;
            }
        }

        /// <summary>
        /// 读取所有压力参数并构建数据帧（16字节）- 从ViewModel获取
        /// </summary>
        // 压力参数帧构建已统一到 ViewModels/MainViewModel.BuildPressureParamsData()（含合法性校验），
        // 此处原有的同名重复实现已删除，避免默认值两处维护而不一致。

        /// <summary>
        /// 组包 0x03 阀门排水时序（数据域 12B：param_id 0x0201 + 5×u16 大端 0.1s）。
        /// 数据源为时序图/正常运行排水输入框的秒值，经 ParseSecondsTo0P1 换算。
        /// 固件 fcu_protocol_set_valve_params() 校验：两段开机排水非零、间隔>打开时长、
        /// 关机时长非零（beforepined 与 V5.2 一致），命中则整帧 5 个字段全部不生效。
        /// stop_duration 为固件死参数（可读写但控制逻辑未使用）：取隐藏框当前值
        /// （「读取FCU参数」时已回填固件原值）原样回传，避免整帧写入误改。
        /// </summary>
        private byte[] BuildValveParamsData()
        {
            ushort firstOpen = ParseSecondsTo0P1(tbx_StartupDrainFirstOpen, "电堆吹扫①时长(start_first_duration)");
            ushort secondOpen = ParseSecondsTo0P1(tbx_StartupDrainSecondOpen, "电堆吹扫②时长(start_second_duration)");
            ushort normalInterval = ParseSecondsTo0P1(tbx_ValveNormalInterval, "排水间隔(normal_interval)");
            ushort normalOpen = ParseSecondsTo0P1(tbx_ValveNormalOpen, "排水打开时长(normal_duration)");
            ushort shutdownOpen = ParseRequiredField(tbx_ValveShutdownOpen, "关机排水时长(stop_duration)", 1, ushort.MaxValue, "0.1s");

            if (normalInterval <= normalOpen)
            {
                tbx_ValveNormalInterval.Focus();
                tbx_ValveNormalInterval.SelectAll();
                throw new ArgumentException(
                    $"排水间隔({normalInterval / 10.0:F1}s)必须大于排水打开时长({normalOpen / 10.0:F1}s)，固件会拒绝整帧");
            }

            byte[] data = new byte[12];
            data[0] = 0x02; data[1] = 0x01;
            data[2] = (byte)(firstOpen >> 8); data[3] = (byte)(firstOpen & 0xFF);
            data[4] = (byte)(secondOpen >> 8); data[5] = (byte)(secondOpen & 0xFF);
            data[6] = (byte)(normalInterval >> 8); data[7] = (byte)(normalInterval & 0xFF);
            data[8] = (byte)(normalOpen >> 8); data[9] = (byte)(normalOpen & 0xFF);
            data[10] = (byte)(shutdownOpen >> 8); data[11] = (byte)(shutdownOpen & 0xFF);

            return data;
        }

        /// <summary>
        /// 读取所有DCDC参数并构建数据帧（6字节）
        /// </summary>
        private byte[] BuildDCDCParamsData()
        {
            // 下位机 fcu_handle_set_dcdc() 要求两者非 0 且 最低工作电压 > 放电截至电压
            ushort minVoltage = ParseRequiredField(tbx_DCDCMinVoltage, "最低工作电压", 1, ushort.MaxValue, "0.1V");
            ushort stopVoltage = ParseRequiredField(tbx_DCDCStopVoltage, "放电截至电压", 1, ushort.MaxValue, "0.1V");

            if (minVoltage <= stopVoltage)
            {
                throw new ArgumentException(
                    $"最低工作电压({minVoltage})必须大于放电截至电压({stopVoltage})，固件会拒绝整帧");
            }

            byte[] data = new byte[6];
            data[0] = 0x03; data[1] = 0x01;
            data[2] = (byte)(minVoltage >> 8); data[3] = (byte)(minVoltage & 0xFF);
            data[4] = (byte)(stopVoltage >> 8); data[5] = (byte)(stopVoltage & 0xFF);

            return data;
        }

        /// <summary>
        /// 计算CRC16-CCITT校验
        /// </summary>
        private ushort CalculateCRC16(byte[] data, int length)
        {
            ushort crc = ProtocolConstants.CRC_INIT_VALUE;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ ProtocolConstants.CRC_POLYNOMIAL);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        /// <summary>
        /// 发送帧数据
        /// </summary>
        private bool SendFrame(byte[] frame)
        {
            // 固件升级模式期间拦截一切 AA55 发送 [BL设计 §4.2 集成点2]
            // APP探活后的正常退出仍持普通锁；只允许归还已保存owner，不提前放开普通控制。
            bool savedOwnerReturn = !_blUpgradeMode && _bmsReturnInFlight && _bmsBusMayBeOwned &&
                string.Equals(_viewModel?.GetSerialPortManager().PortName, _bmsBusOwnerPort, StringComparison.OrdinalIgnoreCase) &&
                FirmwareMainProtocol.IsResponse(frame, _bmsBusOwnerAddress, ProtocolConstants.FUNC_SET_BMS_POLL, 1) && frame[5] == 1;
            if (_blUpgradeMode || (_upgradePrecheckMode && !savedOwnerReturn))
                return false;

            // 使用ViewModel的SerialPortManager发送数据
            var serialPortManager = _viewModel?.GetSerialPortManager();
            return serialPortManager?.SendData(frame) == true;
        }

        /// <summary>
        /// 固件升级模式下的 AA55 帧直发 (升级页 S1 前置检查 / S8 存活确认专用,
        /// 此时 _blUpgradeMode=false, 走正常通道; TX 日志由 SerialPortManager.FrameSent
        /// 事件统一记录, 此处不重复追加) [BL设计 §4.2]
        /// </summary>
        private bool SendAppFrameRaw(byte[] frame)
        {
            var serialPortManager = _viewModel?.GetSerialPortManager();
            return serialPortManager?.SendData(frame) == true;
        }

        private void OnFrameSent(byte[] frame)
        {
            if (frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55 &&
                frame[3] is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x0B)
                _lastSharedSetAt = Environment.TickCount64;

            /* 升级模式下的 BL 二进制帧不进 AA55 通信日志 [批次2审查 P2] */
            if (_blUpgradeMode)
                return;
            AppendCommunicationFrame(CommunicationDirection.Tx, frame);
        }

        private void AppendCommunicationFrame(CommunicationDirection direction, byte[] frame)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    () => AppendCommunicationFrame(direction, frame),
                    DispatcherPriority.Background);
                return;
            }

            _viewModel?.AppendCommunicationFrame(direction, frame);

            if (chk_AutoScrollCommunicationLog.IsChecked == true &&
                lbx_CommunicationLog.Items.Count > 0)
            {
                lbx_CommunicationLog.ScrollIntoView(
                    lbx_CommunicationLog.Items[lbx_CommunicationLog.Items.Count - 1]);
            }
        }

        #endregion

        #region 串口数据接收处理

        /// <summary>
        /// RX 线程 → UI 线程编组（异步 BeginInvoke，不阻塞串口事件循环线程）。
        /// [审查修复 P1] 同步 Dispatcher.Invoke 在 SerialPort.Close/Dispose 的 join 序列期间
        /// 与在途帧互等会死锁整机（.NET 8 SerialStream.Dispose 会 join 事件循环线程），
        /// 因此 RX 链上的 UI 更新一律走本方法；try-catch 保持原先 Invoke 异常就地收容的语义
        /// （原先沿 OnFrameReceived 的 catch 上报，现改为状态栏提示，功能等价）。
        /// </summary>
        private void MarshalRxToUi(Action action)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"界面更新异常: {ex.Message}");
                }
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// 串口数据接收事件
        /// </summary>
        private void SerialPort_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort? sp = sender as SerialPort;
                if (sp == null)
                    return;

                /* 固件升级模式: 字节全部喂入升级客户端, 不进 AA55 解析器与 BMS 服务 [BL设计 §4.2 集成点1] */
                if (_blUpgradeMode)
                {
                    while (sp.BytesToRead > 0)
                    {
                        FirmwareUpgradeView.FeedSerialByte((byte)sp.ReadByte());
                    }
                    return;
                }

                if (_protocolHandler == null)
                    return;

                List<byte>? rxBytes = null;
                while (sp.BytesToRead > 0)
                {
                    byte b = (byte)sp.ReadByte();
                    _protocolHandler.ProcessReceivedByte(b);
                    (rxBytes ??= new List<byte>()).Add(b);
                }

                // Modbus 字节流并行喂入 BMS 服务 (转到UI线程, 保持BmsService单线程)
                if (rxBytes != null && _bmsService != null)
                {
                    byte[] copy = rxBytes.ToArray();
                    Dispatcher.BeginInvoke(() => _bmsService.AppendReceivedBytes(copy));
                }
            }
            catch (Exception ex)
            {
                // 串口拔出、句柄失效等会走到这里，静默会让用户以为设备还在通信
                Dispatcher.BeginInvoke(() => UpdateStatus($"串口接收异常: {ex.Message}"));
            }
        }

        /// <summary>
        /// 帧接收完成处理
        /// </summary>
        private void OnFrameReceived(byte[] frame)
        {
            AppendCommunicationFrame(CommunicationDirection.Rx, frame);

            // 广播给固件升级页 (S1 前置检查读 0x87 / S8 存活确认等 0x81) [BL设计 §4.2]
            BlAppFrameReceived?.Invoke(frame);

            // [轮询修复] 广播探针响应判定: 帧地址 0x00 即探针回包
            // (正常轮询/读取用配置地址, 只有诊断探针用 0 地址; RX 线程调用)。
            // [审查修复 P3] 写序: 先写 func 再清 pending (volatile 释放序),
            // 保证 UI 侧读到 pending=false 时 func 已可见
            if (_diagProbePending && frame.Length > 4 && frame[2] == 0x00)
            {
                _diagProbeFunc = frame[3];
                _diagProbePending = false;
            }

            byte functionCode = 0;
            try
            {
                functionCode = ResponseParser.GetFunctionCode(frame);
                if ((_currentCalibration?.IsBusy == true && functionCode == 0x82) ||
                    (functionCode == 0x8A && frame.Length >= 10 && frame[5] == 3 && frame[6] == 2))
                {
                    int calibrationEpoch = _currentCalibration?.Epoch ?? -1;
                    Dispatcher.BeginInvoke(() => ReceiveCurrentCalibration(frame, calibrationEpoch));
                    return;
                }
                if (_standbyPeriod?.IsBusy == true && (functionCode == 0x82 ||
                    (functionCode == 0x8A && frame.Length >= 10 && frame[5] == 9 && frame[6] == 2)))
                {
                    int standbyEpoch = _standbyPeriod.Epoch;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_standbyPeriod.Epoch != standbyEpoch) return;
                        if (_standbyPeriod.Receive(frame) && _standbyPeriod.ActualPeriod.HasValue)
                            tbx_StandbyPeriod.Text = _standbyPeriod.ActualPeriod.Value.ToString();
                        RenderStandbyPeriod();
                    });
                    return;
                }

                switch (functionCode)
                {
                    case 0x81: // 设备序列号响应
                        HandleSerialNumberResponse(frame);
                        break;

                    case 0x82: // FCU参数设置响应(0x04 类统一响应码; 有在途 0x0504 指令时走专用处理器)
                        HandleLoadManualResponse(frame);
                        break;

                    case 0x87: // FCU电堆信息响应
                        HandleStackInfoResponse(frame);
                        break;

                    case 0x88: // 启动响应
                        HandleStartResponse(frame);
                        break;

                    case 0x89: // 关闭响应
                        HandleStopResponse(frame);
                        break;

                    case 0x8A: // 配置参数响应
                        HandleConfigParamResponse(frame);
                        break;

                    case 0x8C: // 修改设备地址响应
                        HandleSetDeviceAddressResponse(frame);
                        break;

                    case ProtocolConstants.RESP_BMS_POLL: // BMS轮询使能响应 [SOC计划 §4.6]
                        HandleBmsPollResponse(frame);
                        break;

                    case ProtocolConstants.RESP_FLOW_PARAMS: // 工作流程参数设置响应(1B结果码)
                        HandleSetParamResponse(frame);
                        break;

                    case ProtocolConstants.RESP_FAN_MANUAL: // 风扇手动运行控制响应(1B结果码)
                        HandleFanManualResponse(frame);
                        break;

                    case ProtocolConstants.RESP_SOC_PARAMS: // BMS/SOC参数设置响应(1B结果码)
                        HandleSocParamsSetResponse(frame);
                        break;

                    case ProtocolConstants.RESP_BMS_CACHE: // 当前主线0x91为18B缓存，旧IAP 1B不会被接收
                        int cacheEpoch = _bmsCache?.Epoch ?? -1;
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_bmsCache?.Epoch != cacheEpoch) return;
                            if (!DirectBmsMode && _bmsCache?.Receive(frame) == true) RenderBmsCache();
                        });
                        break;

                    case 0xF0: // 报警信息上报
                        HandleAlarmReport(frame);
                        break;

                    case 0xF1: // 状态变更上报
                        HandleStateChangeReport(frame);
                        break;

                    case 0xF2: // 拉载状态上报
                        HandleLoadStatusReport(frame);
                        break;

                    default:
                        UpdateStatus($"收到未知响应码 0x{functionCode:X2}，已忽略");
                        break;
                }
            }
            catch (Exception ex)
            {
                // 不能静默吞掉：这层 catch 之下是全部响应解析与 UI 更新，
                // 静默会表现为「点了读取但某几个框不更新」且无任何线索，极难定位
                UpdateStatus($"响应处理异常 (功能码 0x{functionCode:X2}): {ex.Message}");
            }
        }

        /// <summary>
        /// 帧校验失败处理
        /// </summary>
        private void OnFrameValidationFailed()
        {
            MarshalRxToUi(() =>
            {
                UpdateStatus("接收数据异常");
            });
        }

        #endregion

        #region 响应处理方法

        /// <summary>
        /// 处理设备序列号响应
        /// </summary>
        private void HandleSerialNumberResponse(byte[] frame)
        {
            string serialNumber = ResponseParser.ParseSerialNumber(frame);
            MarshalRxToUi(() =>
            {
                if (_viewModel != null)
                {
                    _viewModel.SerialNumber = serialNumber;
                    _viewModel.StatusMessage = "设备序列号读取成功";
                }
            });
        }

        /// <summary>
        /// 处理电堆信息响应
        /// </summary>
        private void HandleStackInfoResponse(byte[] frame)
        {
            StackInfoData data = ResponseParser.ParseStackInfo(frame);
            // [轮询修复] 收到 0x87 即链路存活, 清除轮询未命中计数 (RX 线程调用, 字段为 volatile)
            _awaitingStackInfo = false;
            _stackInfoMissCount = 0;
            // 更新功率曲线图数据（DCDC电压/电流）
            UpdateStackInfo(data);
            bool isDiagProbe = frame.Length > 2 && frame[2] == 0x00;
            MarshalRxToUi(() =>
            {
                if (_viewModel != null)
                {
                    UpdateViewModelStackInfo(data);
                    // 广播回包是诊断探针的应答, 不与正常读取的成功提示混淆
                    _viewModel.StatusMessage = isDiagProbe
                        ? "诊断探针已获 FCU 响应 (广播地址)"
                        : "电堆信息读取成功";
                }
            });
        }

        /// <summary>
        /// 处理参数设置响应
        /// </summary>
        private void HandleSetParamResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseSetParamResponse(frame);
            MarshalRxToUi(() =>
            {
                if (result == 0x00)
                {
                    UpdateStatus("参数设置成功");
                }
                else
                {
                    UpdateStatus($"参数设置失败: {ProtocolConstants.GetErrorCodeDescription(result)}");
                }
            });
        }

        /// <summary>
        /// 处理 BMS/SOC 参数设置响应（0x90）。成功表示参数已固化，需重启 FCU 生效。
        /// </summary>
        private void HandleSocParamsSetResponse(byte[] frame)
        {
            if (_pendingSocWriteDeviceAddress.HasValue &&
                (frame.Length <= 2 || frame[2] != _pendingSocWriteDeviceAddress.Value))
            {
                /* 在接收线程快照期望地址（编组回调内字段可能已被超时 Tick/断线清理置 null） */
                byte expectedAddress = _pendingSocWriteDeviceAddress.Value;
                byte actualAddress = frame.Length > 2 ? frame[2] : (byte)0;
                MarshalRxToUi(() => UpdateStatus(
                    $"忽略地址不匹配的SOC写入响应：期望{expectedAddress}，收到{actualAddress}"));
                return;
            }

            byte result = ResponseParser.ParseSocParamsSetResponse(frame);
            MarshalRxToUi(() =>
            {
                if (_pendingSocWriteDeviceAddress.HasValue)
                {
                    StopSocResponseTimeout();
                }

                if (result != ProtocolConstants.ERR_SUCCESS)
                {
                    string error = ProtocolConstants.GetErrorCodeDescription(result);
                    bool responseMalformed = result == 0xFF;
                    if (result == 0xFF)
                    {
                        _socParamsAwaitingRestart = false;
                        _socWriteResultUnknown = true;
                    }
                    _pendingSocWrite = null;
                    _pendingSocWriteDeviceAddress = null;
                    _pendingSocWritePortName = null;
                    _pendingSocReadDeviceAddress = null;
                    _socWriteReadbackPending = false;
                    tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    tbk_SocValidationStatus.Text = responseMalformed
                        ? "SOC写入响应异常，设备写入结果和下次重启生效值未知。"
                        : $"SOC参数写入失败：{error}";
                    UpdateStatus(responseMalformed
                        ? "BMS/SOC写入响应异常，设备结果未知"
                        : $"BMS/SOC参数设置失败：{error}");
                    return;
                }

                if (_pendingSocWrite == null)
                {
                    tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    tbk_SocValidationStatus.Text = "FCU确认写入成功，但上位机没有对应的待核对参数；未自动回读。";
                    UpdateStatus("收到无法匹配请求的BMS/SOC写入成功响应");
                    return;
                }

                _socParamsAwaitingRestart = true;
                _socWriteResultUnknown = false;
                _socWriteReadbackPending = true;
                tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                tbk_SocValidationStatus.Text = "SOC参数已写入Flash；重启FCU后生效，正在自动回读核对。";
                UpdateStatus("BMS/SOC参数已固化，重启FCU后生效；正在回读");

                if (_pendingSocWriteDeviceAddress.HasValue)
                {
                    if (!SendSocReadConfigCommand(_pendingSocWriteDeviceAddress.Value))
                    {
                        tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                        tbk_SocValidationStatus.Text =
                            "SOC写入已确认，但自动回读命令发送失败；写入快照已保留，可再次点击读取SOC参数核对。";
                        UpdateStatus("BMS/SOC写入已固化，但自动回读发送失败");
                    }
                }
            });
        }

        /// <summary>
        /// 处理风扇手动运行控制响应（0x8F，数据域1B结果码）。
        /// 只有FCU确认成功后才把界面状态切换为手动或自动。
        /// </summary>
        private void HandleFanManualResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseFanManualResponse(frame);

            MarshalRxToUi(() =>
            {
                // 与 SOC 超时处理器同款：计时器停止与 pending 清理统一编组到 UI 线程
                StopFanManualResponseTimeout();
                bool? requestedEnable = _pendingFanManualEnable;
                byte requestedDuty = _pendingFanManualDuty;
                _pendingFanManualEnable = null;
                if (result == ProtocolConstants.ERR_SUCCESS)
                {
                    if (requestedEnable == null)
                    {
                        tbk_FanManualMode.Text = "FCU已确认控制（当前模式无回读）";
                        tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
                        UpdateStatus("风扇运行控制成功，但未找到对应的待确认请求");
                    }
                    else if (requestedEnable == true)
                    {
                        tbk_FanManualMode.Text = $"已确认手动请求：{requestedDuty}%（FCU重启或超温保护会自动恢复温控）";
                        tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                        UpdateStatus($"风扇手动控制已生效：{requestedDuty}%");
                    }
                    else
                    {
                        tbk_FanManualMode.Text = "自动温控（PEM闭环）";
                        tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                        UpdateStatus("风扇已恢复自动温控");
                    }
                }
                else
                {
                    string message = ProtocolConstants.GetErrorCodeDescription(result);
                    tbk_FanManualMode.Text = $"控制失败：{message}";
                    tbk_FanManualMode.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    HC.Growl.Error($"风扇控制失败：{message}");
                    UpdateStatus($"风扇控制失败：{message}");
                }
            });
        }

        /// <summary>
        /// 处理启动响应
        /// </summary>
        private void HandleStartResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            MarshalRxToUi(() =>
            {
                string message = result switch
                {
                    0x00 => "电堆启动成功",
                    0x01 => "启动失败：高压压力低于高压最小值",
                    0x02 => "启动失败：设备忙（非关机态）",
                    // 下位机 fcu_control_check_start_conditions() 有三条路径都返回 0x03，
                    // 协议无法区分，这里把三种可能都列出，并在 SOC 已知超限时直接点名
                    0x03 => BuildStartFail03Message(),
                    _ => $"启动响应异常 (错误码: 0x{result:X2})"
                };
                UpdateStatus(message);
            });
        }

        /// <summary>
        /// 组装启动失败 0x03 的提示文案。
        /// 下位机 0x03 覆盖三种条件（低压超区间 / 电堆温度≥风扇上限 / SOC超过被动自启阈值被拒启），
        /// 响应码本身无法区分，若已知 SOC 超限则直接点名，否则列出全部可能。
        /// </summary>
        private string BuildStartFail03Message()
        {
            var bms = _bmsService?.Data;
            if (bms != null && bms.Online && bms.Soc > _socStartMax)
            {
                return $"启动失败：SOC={bms.Soc}% 超过被动自启阈值(拒启) {_socStartMax}%";
            }

            return "启动失败：启动条件不满足（低压超出上下限 / 电堆温度≥风扇温度上限 / "
                 + $"SOC>{_socStartMax}% 拒启），请检查「系统监控」中的压力与温度";
        }

        /// <summary>
        /// 处理关闭响应
        /// </summary>
        private void HandleStopResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            MarshalRxToUi(() =>
            {
                if (result == 0x00)
                {
                    UpdateStatus("电堆关闭成功");
                }
                else
                {
                    UpdateStatus($"电堆关闭失败 (错误码: 0x{result:X2})");
                }
            });
        }

        /// <summary>
        /// 处理修改设备地址响应 (功能码0x8C)
        /// </summary>
        private void HandleSetDeviceAddressResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            MarshalRxToUi(() =>
            {
                if (result == 0x00)
                {
                    MessageBox.Show("设备地址修改成功！\n\n请重启设备使新地址生效。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatus("设备地址修改成功，请重启设备");
                }
                else
                {
                    string errorMessage = result switch
                    {
                        ProtocolConstants.ERR_ADDRESS_INVALID => "地址无效（必须在1-247范围内）",
                        ProtocolConstants.ERR_SAVE_FAILED => "保存到Flash失败",
                        _ => $"未知错误(0x{result:X2})"
                    };
                    MessageBox.Show($"设备地址修改失败：{errorMessage}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus($"设备地址修改失败: {errorMessage}");
                }
            });
        }

        /// <summary>
        /// 处理 BMS 轮询使能响应 (0x8D, 数据域1B结果码) [SOC计划 §4.6]
        /// </summary>
        private void HandleBmsPollResponse(byte[] frame)
        {
            if (!FirmwareMainProtocol.IsResponse(frame, _bmsExchangeAddress, ProtocolConstants.RESP_BMS_POLL, 1)) return;
            byte result = ResponseParser.ParseSetParamResponse(frame);
            bool success = result == ProtocolConstants.ERR_SUCCESS;

            MarshalRxToUi(() =>
            {
                // 成败分离 [审查修复 P2]: 收到即算 ack (等待环退出), 仅成功才置 AckSuccess
                _bmsPollAckReceived = true;
                _bmsPollAckSuccess = success;
                if (success)
                {
                    UpdateStatus("BMS 轮询使能设置成功");
                }
                else
                {
                    string message = ProtocolConstants.GetErrorCodeDescription(result);
                    HC.Growl.Error($"BMS 轮询使能设置失败: {message}");
                    UpdateStatus($"BMS 轮询使能设置失败: {message}");
                }
            });
        }

        #region BMS 总线仲裁 (0x0D set_bms_poll)

        /// <summary>
        /// 发送 BMS 轮询使能 (0x0D): 上位机接管/归还 BMS 总线
        /// [SOC计划 §1.3] en=0 上位机接管(停固件轮询), en=1 恢复固件轮询
        /// 注意: 固件每次写Flash整页擦写(fcu_protocol.c set_bms_poll),
        ///       仅允许会话级调用(连接/断开各一次), 禁止逐次握手 [上位机优化计划 D1]
        /// </summary>
        private bool SendBmsPollEnable(byte en)
        {
            if (!_isSerialOpen)
                return false;

            // 地址框非法时安全跳过, 不再让 FormatException 沿 Open 事件链
            // 冒泡成"打开串口失败" (物理上串口已开) [审查修复 P3]
            byte deviceAddress;
            if (en == 1 && _bmsBusMayBeOwned) deviceAddress = _bmsBusOwnerAddress;
            else if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out deviceAddress, showError: false))
            {
                UpdateStatus("设备地址非法, 未发送 BMS 轮询使能指令");
                return false;
            }

            string? port = _viewModel?.GetSerialPortManager().PortName;
            if (en == 0 && _bmsBusMayBeOwned &&
                (!string.Equals(port, _bmsBusOwnerPort, StringComparison.OrdinalIgnoreCase) || deviceAddress != _bmsBusOwnerAddress))
            {
                UpdateStatus("原设备BMS轮询尚未确认归还，请先连接原串口和地址归还，再接管其他设备");
                return false;
            }
            if (en == 1 && _bmsBusMayBeOwned)
            {
                if (!string.Equals(port, _bmsBusOwnerPort, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("原BMS总线接管来自其他串口，请连接原设备归还轮询");
                    return false;
                }
                deviceAddress = _bmsBusOwnerAddress;
            }
            _bmsExchangeAddress = deviceAddress;
            bool sent = SendFrame(BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_BMS_POLL, new[] { en }));
            if (sent && en == 0)
            {
                _bmsBusMayBeOwned = true;
                _bmsBusOwnerAddress = deviceAddress;
                _bmsBusOwnerPort = port;
            }
            return sent;
        }

        /// <summary>
        /// [审查修复 P2] 连接时接管 BMS 总线 (0x0D en=0) 并等 0x8D 成功确认, 最多 3 次;
        /// 结果记入 _bmsTakeoverConfirmed 供 BMS 轮询启动门控。
        /// 固件依据: 固件自轮询 BMS 的等待窗口 (fcu_modbus.c g_modbus_rx_active,
        /// 无 BMS 应答时每 2s 有约 200ms) 会把窗口内到达的 AA55 字节喂进 Modbus 缓冲
        /// (fcu_protocol.c:486-488), 单发 en=0 有概率被吞且整个会话无法恢复。
        /// 无成功 0x8D = 指令未被接受 = 通常未写 Flash (fcu_handle_set_bms_poll 成功才写;
        /// 仅应答帧丢失的例外会多一次整页擦写, 会话级有界), 重试磨损可忽略;
        /// 300ms 间隔大于单个吞帧窗口(200ms), 3 次内必有落点。
        /// </summary>
        private void TakeOverBmsBusWithConfirm()
        {
            // 泵送重入互斥: "断开→重连"嵌套连接事件时外层交换仍在进行, 静默跳过
            if (_bmsBusExchangeInProgress)
                return;

            int generation = _bmsExchangeGeneration;
            string? port = _viewModel?.GetSerialPortManager().PortName;
            string addressText = GetDeviceAddress();
            bool IsCurrent() => generation == _bmsExchangeGeneration && DirectBmsMode && _isSerialOpen &&
                !_bmsReturnInFlight && string.Equals(port, _viewModel?.GetSerialPortManager().PortName, StringComparison.OrdinalIgnoreCase) &&
                addressText == GetDeviceAddress();
            if (!IsCurrent()) return;
            _bmsBusExchangeInProgress = true;
            try
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    // 泵送期间用户可能已点断开/关窗: 静默退出 (归还流程随后会发 en=1)
                    if (!IsCurrent())
                        return;

                    _bmsPollAckReceived = false;
                    _bmsPollAckSuccess = false;
                    if (!SendBmsPollEnable(0))
                        continue;   // 发送失败(串口已关/地址非法): 不空等 300ms, 直接下一轮判定

                    // DoEvents 泵送使 0x8D 确认帧可被接收线程处理 (与断开路径同款等待模式)。
                    // 断开路径归还 en=1 的 0x8D 也会点亮 AckReceived, 必须同时查
                    // AckSuccess 与连接状态防误报 [审查修复 P2]
                    int start = Environment.TickCount;
                    while (!_bmsPollAckReceived && Environment.TickCount - start < 300)
                    {
                        if (!IsCurrent())
                            return;
                        DoEvents();
                        Thread.Sleep(10);
                    }

                    if (!IsCurrent()) return;
                    if (IsCurrent() && _bmsPollAckReceived && _bmsPollAckSuccess)
                    {
                        _bmsTakeoverConfirmed = true;
                        UpdateStatus($"BMS 总线接管成功 (第{attempt}次尝试获 FCU 确认)");
                        return;
                    }
                }

                _bmsTakeoverConfirmed = false;
                HC.Growl.Warning("BMS 总线接管未获 FCU 确认 (3 次无成功 0x8D 应答): FCU 可能仍在自行轮询 BMS, " +
                                 "其等待窗口会间歇吞掉电堆信息请求; 也可能是设备地址与 FCU 不匹配。建议核对设备地址后断开重新连接");
                UpdateStatus("BMS 总线接管未确认, FCU 可能仍占用总线或地址不匹配");
            }
            finally
            {
                _bmsBusExchangeInProgress = false;
            }
        }

        /// <summary>
        /// 手动归还 BMS 总线给 FCU (0x0D en=1)。
        /// 固件把该使能位持久化到 Flash，上位机若异常退出(崩溃/掉线)，en=0 会残留，
        /// 导致 FCU 重启后仍不轮询 BMS，SOC 门控与 BMS 故障耦合永久静默失效。
        /// 此按钮提供在正常关闭路径之外的自救手段。
        /// </summary>
        private void btn_ReturnBmsPoll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return;
            }

            CancelBmsTakeover();

            var confirm = MessageBox.Show(
                "将向 FCU 发送「启用 BMS 轮询」(0x0D en=1)，恢复固件自身的 BMS Modbus 轮询。\n\n" +
                "注意：\n" +
                "· 固件会写 Flash（整页擦写），请勿频繁操作\n" +
                "· 恢复后 FCU 与上位机会同时读 BMS，可能出现总线竞争\n" +
                "· 建议仅在上位机异常退出后、需要恢复 FCU 侧门控时使用\n\n" +
                "确定继续？",
                "归还 BMS 总线", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK)
                return;

            // 停止上位机自身轮询，避免归还后双主站同时抢总线
            _bmsService?.StopPolling();

            _bmsReturnInFlight = true;
            try
            {
                _bmsPollAckReceived = false;
                _bmsPollAckSuccess = false;
                SendBmsPollEnable(1);

                int start = Environment.TickCount;
                while (!_bmsPollAckReceived && Environment.TickCount - start < 300)
                {
                    DoEvents();
                    Thread.Sleep(10);
                }

                if (_bmsPollAckReceived && _bmsPollAckSuccess)
                {
                    _bmsTakeoverConfirmed = false;   // 总线已归还, 本会话接管状态失效
                    _bmsBusMayBeOwned = false;
                    UpdateStatus("已归还 BMS 总线，FCU 侧轮询已启用");
                    HC.Growl.Success("FCU 已确认启用 BMS 轮询");
                }
                else
                {
                    UpdateStatus("归还 BMS 总线：未收到 FCU 成功确认");
                    HC.Growl.Warning("未收到 FCU 的成功 0x8D 确认，请检查连接后重试");
                }
            }
            finally
            {
                _bmsReturnInFlight = false;
            }
        }

        /// <summary>
        /// 关闭串口前: 归还 BMS 总线 (发 en=1 并等 0x8D 成功确认, 最多 2 次尝试)。
        /// [审查修复 P2] en 位持久化在 FCU Flash——归还未确认时 FCU 重新上电也不会恢复轮询
        /// (en=0 残留), 故重试 1 次; 指令丢失时固件不会写 Flash, 重试无额外磨损。
        /// </summary>
        private void OnBeforeSerialClose()
        {
            if (!_isSerialOpen || !_bmsBusMayBeOwned)
                return;

            _bmsExchangeGeneration++;
            _bmsReturnInFlight = true;   // 通知在途接管泵送中止, 保证 en=1 是最后的 0x0D
            try
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (!_isSerialOpen)
                        break;

                    _bmsPollAckReceived = false;
                    _bmsPollAckSuccess = false;
                    if (SendBmsPollEnable(1))
                    {
                        // 9600bps 收发各8B约20ms, 300ms裕量充足; DoEvents泵送使确认帧可被处理
                        int start = Environment.TickCount;
                        while (!_bmsPollAckReceived && Environment.TickCount - start < 300)
                        {
                            if (!_isSerialOpen)
                                break;
                            DoEvents();
                            Thread.Sleep(10);
                        }
                    }

                    if (_bmsPollAckReceived && _bmsPollAckSuccess)
                    {
                        _bmsBusMayBeOwned = false;
                        _bmsTakeoverConfirmed = false;
                        return;
                    }
                }

                // 文案不含"重新上电": en 位在 Flash, 重启无法清除残留 [审查修复 P2]
                HC.Growl.Warning("BMS 总线归还未获 FCU 成功确认: en=0 可能残留在 FCU Flash, FCU 重启也不会自动恢复轮询。\n" +
                                 "请重新连接后正常断开一次, 或在 BMS 页使用「归还 BMS 总线」按钮");
                UpdateStatus("BMS 总线归还未确认, en=0 可能残留 Flash");
            }
            finally
            {
                _bmsReturnInFlight = false;
            }
        }

        /// <summary>
        /// 窗口关闭前: 若串口仍连接, 先归还 BMS 总线
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            /* 固件升级进行中禁止关窗 [BL设计 §4.4 中途关窗拦截]:
             * 互斥窗口(S1末~S8末)由 _blUpgradeMode 拦截; S8 之后退出归一化期间(6s 静默等)
             * 由升级页 _busy 拦截 [全量终审 P2] */
            if (_blUpgradeMode || _upgradePrecheckMode || FirmwareUpgradeView.IsBusy)
            {
                HC.Growl.Warning("固件升级进行中, 请先在升级页点击[取消]完成退出流程后再关闭程序");
                e.Cancel = true;
                return;
            }

            if (_isSerialOpen)
            {
                OnBeforeSerialClose();
            }
            _firmwareMainTimer?.Stop();
            base.OnClosing(e);
        }

        /// <summary>
        /// 泵送 Dispatcher 消息队列 (等确认帧期间保持 UI 与串口事件可处理)
        /// </summary>
        private static void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(f =>
                {
                    ((DispatcherFrame)f!).Continue = false;
                    return null;
                }), frame);
            Dispatcher.PushFrame(frame);
        }

        #endregion

        /// <summary>
        /// 处理配置参数响应
        /// </summary>
        private void HandleConfigParamResponse(byte[] frame)
        {
            ushort paramTypeID = ResponseParser.GetParameterTypeID(frame);

            MarshalRxToUi(() =>
            {
                if (paramTypeID == ProtocolConstants.PARAM_TYPE_SOC)
                {
                    if (!_pendingSocReadDeviceAddress.HasValue)
                    {
                        UpdateStatus("收到无对应读取请求的SOC参数响应，已忽略");
                        return;
                    }

                    byte actualAddress = frame.Length > 2 ? frame[2] : (byte)0;
                    if (actualAddress != _pendingSocReadDeviceAddress.Value)
                    {
                        UpdateStatus(
                            $"忽略地址不匹配的SOC读取响应：期望{_pendingSocReadDeviceAddress.Value}，收到{actualAddress}");
                        return;
                    }

                    StopSocResponseTimeout();
                }

                // 先校验再解析：Parse*Params 在长度不符时会静默返回全 0 对象，
                // 若直接更新 UI，用户会看到一片 0 却被告知「读取成功」，无法察觉解析失败
                if (!ResponseParser.TryValidateReadConfigResponse(frame, paramTypeID,
                        out string failReason))
                {
                    if (paramTypeID == ProtocolConstants.PARAM_TYPE_SOC)
                    {
                        _pendingSocReadDeviceAddress = null;
                    }
                    string msg = paramTypeID == ProtocolConstants.PARAM_TYPE_VALVE_TIMING
                        ? $"配置参数 0x0202 读取失败：{failReason}——固件可能不支持扩展时序（需包含0x0202扩展的固件，最新main已包含）"
                        : $"配置参数 0x{paramTypeID:X4} 解析失败：{failReason}";
                    UpdateStatus(msg);
                    HC.Growl.Warning(msg);
                    return;
                }

                switch (paramTypeID)
                {
                    case 0x0101: // 压力参数
                        var pressureParams = ResponseParser.ParsePressureParams(frame);
                        UpdateViewModelPressureParams(pressureParams);
                        break;

                    case 0x0201: // 阀门参数
                        var valveParams = ResponseParser.ParseValveParams(frame);
                        UpdateValveParams(valveParams);
                        break;

                    case ProtocolConstants.PARAM_TYPE_VALVE_TIMING: // 阀门扩展时序(0x0202)
                        var valveTimingParams = ResponseParser.ParseValveTimingParams(frame);
                        UpdateValveTimingParams(valveTimingParams);
                        break;

                    case 0x0301: // DCDC参数
                        var dcdcParams = ResponseParser.ParseDCDCParams(frame);
                        UpdateDCDCParams(dcdcParams);
                        break;

                    case 0x0401: // 风扇温度参数
                        var fanTempParams = ResponseParser.ParseFanTempParams(frame);
                        UpdateFanTempParams(fanTempParams);
                        break;

                    case 0x0501: // 拉载时序参数
                        var loadSeqParams = ResponseParser.ParseLoadSequenceParams(frame);
                        UpdateLoadSequenceParams(loadSeqParams);
                        UpdateRecipeTextBoxes(loadSeqParams);
                        break;

                    case 0x0502: // 拉载控制参数
                        var loadCtrlParams = ResponseParser.ParseLoadControlParams(frame);
                        UpdateLoadControlParams(loadCtrlParams);
                        break;

                    case 0x0503: // 拉载启动参数
                        var loadStartParams = ResponseParser.ParseLoadStartParams(frame);
                        UpdateLoadStartParams(loadStartParams);
                        break;

                    case 0x0601: // 工作流程参数
                        var flowParams = ResponseParser.ParseFlowParams(frame);
                        UpdateFlowParams(flowParams);
                        break;

                    case ProtocolConstants.PARAM_TYPE_SOC: // BMS/SOC参数
                        var socParams = ResponseParser.ParseSocParams(frame);
                        bool socUpdated = UpdateSocParams(socParams);
                        _pendingSocReadDeviceAddress = null;
                        if (!socUpdated)
                        {
                            return;
                        }
                        break;

                    case 0x0901: // 通用参数
                        var generalParams = ResponseParser.ParseGeneralParams(frame);
                        UpdateGeneralParams(generalParams);
                        break;

                    default:
                        // 校验已通过但此处无分支，说明新增了参数ID却漏了 UI 更新
                        UpdateStatus($"参数 0x{paramTypeID:X4} 已收到但无对应界面更新逻辑");
                        return;
                }

                UpdateStatus($"配置参数 0x{paramTypeID:X4} 读取成功");
            });
        }

        /// <summary>
        /// 处理报警信息上报
        /// </summary>
        private void HandleAlarmReport(byte[] frame)
        {
            AlarmData alarm = ResponseParser.ParseAlarmInfo(frame);
            MarshalRxToUi(() =>
            {
                ShowAlarmMessage(alarm);
            });
        }

        /// <summary>
        /// 处理状态变更上报
        /// </summary>
        private void HandleStateChangeReport(byte[] frame)
        {
            StateChangeData stateChange = ResponseParser.ParseStateChange(frame);
            MarshalRxToUi(() =>
            {
                ShowStateChange(stateChange);
            });
        }

        #endregion

        #region UI更新方法

        /// <summary>
        /// 电堆工作状态码转中文。对应下位机 fcu_control.h 的 FCU_CTRL_STATE_* 定义。
        /// 供 0x87 电堆信息响应与 0xF1 状态变更上报共用。
        /// </summary>
        private static string GetStackStateName(byte state)
        {
            return state switch
            {
                0 => "关机",
                1 => "启动中",
                2 => "运行",
                3 => "故障",
                4 => "关机中",
                _ => $"未知({state})"
            };
        }

        /// <summary>
        /// 低压泄压保护状态转中文（0xF1 状态码 0x0105，取值 0/1）。
        /// 对应下位机 fcu_protocol.h 的 FCU_STATUS_LOW_PRESSURE_RELIEF 定义。
        /// </summary>
        private static string GetLowPressureReliefStateName(byte state)
        {
            return state switch
            {
                0 => "未触发",
                1 => "泄压中",
                _ => $"未知({state})"
            };
        }

        /// <summary>
        /// 更新ViewModel中的电堆信息（MVVM模式）
        /// </summary>
        private void UpdateViewModelStackInfo(StackInfoData data)
        {
            if (_viewModel == null) return;

            _viewModel.StackState = GetStackStateName(data.WorkState);

            _viewModel.HighPressure = data.HighPressure.ToString();
            _viewModel.LowPressure = data.LowPressure.ToString();
            _viewModel.HydrogenValveState = data.HydrogenValveState == 1 ? "打开" : "关闭";
            _viewModel.DrainValveState = data.DrainValveState == 1 ? "打开" : "关闭";
            _viewModel.DCDCInputVoltage = (data.DCDCInputVoltage / 10.0).ToString("F1");
            _viewModel.DCDCInputCurrent = (data.DCDCInputCurrent / 10.0).ToString("F1");
            _viewModel.DCDCOutputVoltage = (data.DCDCOutputVoltage / 10.0).ToString("F1");
            _viewModel.DCDCOutputCurrent = (data.DCDCOutputCurrent / 10.0).ToString("F1");
            _viewModel.FanSpeed = data.FanSpeed.ToString();
            _viewModel.Fan2EstimatedSpeed = TryEstimateFan2Speed(data, out int estimatedRpm, out int estimatedDuty)
                ? $"{estimatedRpm} ({estimatedDuty}%)"
                : "--";
            _viewModel.StackTemperature = (data.StackTemperature / 10.0).ToString("F1");
            _viewModel.FCUTemperature = (data.FCUTemperature / 10.0).ToString("F1");
            _viewModel.RunTime = data.RunTime.ToString();
            _viewModel.FaultCode = data.FaultCode == 0 ? "无故障" : $"0x{data.FaultCode:X2}";
        }

        private bool UpdateSocParams(SocParams data)
        {
            if (data.StartMax is < 1 or > 100 ||
                data.StopHigh is < 1 or > 100 ||
                data.StopMin is < 1 or > 100 ||
                data.BatteryCapacity == 0)
            {
                string error = $"FCU返回非法SOC参数：{data.StartMax}/{data.StopHigh}/{data.StopMin}/{data.BatteryCapacity}Ah";
                SetSocValidationError(error);
                UpdateStatus(error);
                HC.Growl.Warning(error);
                return false;
            }

            if (_pendingSocWrite != null && !_socWriteReadbackPending)
            {
                const string warning = "SOC写入尚未收到0x90确认，本次回读不作为写入核对，界面输入保持不变";
                UpdateStatus(warning);
                HC.Growl.Warning(warning);
                return false;
            }

            if (_socWriteReadbackPending && _pendingSocWrite != null)
            {
                SocParams expected = _pendingSocWrite;
                bool matches = data.StartMax == expected.StartMax &&
                    data.StopHigh == expected.StopHigh &&
                    data.StopMin == expected.StopMin &&
                    data.BatteryCapacity == expected.BatteryCapacity;

                _pendingSocWrite = null;
                _pendingSocWriteDeviceAddress = null;
                _pendingSocWritePortName = null;
                _socWriteReadbackPending = false;

                if (!matches)
                {
                    string error = "SOC写入后回读不一致：" +
                        $"写入 {expected.StartMax}/{expected.StopHigh}/{expected.StopMin}/{expected.BatteryCapacity}Ah，" +
                        $"设备 {data.StartMax}/{data.StopHigh}/{data.StopMin}/{data.BatteryCapacity}Ah；已保留原输入";
                    SetSocValidationError(error);
                    UpdateStatus(error);
                    HC.Growl.Error(error);
                    return false;
                }

                SetSocTextBoxes(data);
                tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                tbk_SocValidationStatus.Text =
                    $"写入与Flash回读一致：{FormatSocParams(data)}；待重启FCU生效。当前运行提示仍按 自启阈值{_socStartMax}%/停机{_socStopHigh}%。";
                UpdateStatus("BMS/SOC写入与回读一致；待重启FCU生效");
                return true;
            }

            SetSocTextBoxes(data);
            if (_socParamsAwaitingRestart || _socWriteResultUnknown)
            {
                tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
                string activationState = _socParamsAwaitingRestart
                    ? "已确认存在待重启生效的写入"
                    : "此前写入结果未知，重启生效值不能确认";
                tbk_SocValidationStatus.Text =
                    $"已回读Flash存储值：{FormatSocParams(data)}；{activationState}。当前运行提示仍按 自启阈值{_socStartMax}%/停机{_socStopHigh}%。";
                return true;
            }

            ApplyActiveSocParams(data);
            tbk_SocValidationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            tbk_SocValidationStatus.Text = $"已回读FCU当前配置值：{FormatSocParams(data)}。";

            return true;
        }

        private void SetSocTextBoxes(SocParams data)
        {
            tbx_OverviewSocStartMax.Text = data.StartMax.ToString(CultureInfo.InvariantCulture);
            tbx_OverviewSocStopHigh.Text = data.StopHigh.ToString(CultureInfo.InvariantCulture);
            tbx_OverviewSocStopMin.Text = data.StopMin.ToString(CultureInfo.InvariantCulture);
            tbx_OverviewBatteryCapacity.Text = data.BatteryCapacity.ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyActiveSocParams(SocParams data)
        {
            _socStartMax = data.StartMax;
            _socStopHigh = data.StopHigh;
            _socStopMin = data.StopMin;
            tbk_BmsSocThresholdHint.Text =
                $"  (SOC<{_socStartMax} 被动自启 / >{_socStartMax} 拒启 / >{_socStopHigh} 自动停)";

            if (_bmsService?.Data is { Online: true } bms)
            {
                OnBmsDataUpdated(bms);
            }
        }

        private static string FormatSocParams(SocParams data)
        {
            return $"{data.StartMax}/{data.StopHigh}/{data.StopMin}/{data.BatteryCapacity}Ah";
        }

        /// <summary>
        /// 风扇2没有FG转速反馈。两路风扇共用一个占空比，这里复制固件正常温控插值公式，
        /// 再用界面设定的模拟满速换算。固件强制全速计时和瞬时拉载占空比未随状态帧上报，故只能标注为估算。
        /// </summary>
        private bool TryEstimateFan2Speed(StackInfoData data, out int rpm, out int duty)
        {
            rpm = 0;
            duty = 0;

            bool parsedTempMin = decimal.TryParse(tbx_TempMin.Text, NumberStyles.Number,
                    CultureInfo.CurrentCulture, out decimal tempMinDisplay)
                || decimal.TryParse(tbx_TempMin.Text, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out tempMinDisplay);
            bool parsedTempMax = decimal.TryParse(tbx_TempMax.Text, NumberStyles.Number,
                    CultureInfo.CurrentCulture, out decimal tempMaxDisplay)
                || decimal.TryParse(tbx_TempMax.Text, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out tempMaxDisplay);

            if (!int.TryParse(tbx_Fan2MinDuty.Text, out int minDuty) || minDuty < 0 || minDuty > 100 ||
                !int.TryParse(tbx_Fan2MaxDuty.Text, out int maxDuty) || maxDuty < minDuty || maxDuty > 100 ||
                !parsedTempMin || !parsedTempMax || tempMinDisplay >= tempMaxDisplay ||
                !int.TryParse(tbx_Fan2MaxRpm.Text, out int maxRpm) || maxRpm <= 0)
            {
                return false;
            }

            int tempMinRaw = (int)decimal.Round(tempMinDisplay * 10m, 0, MidpointRounding.AwayFromZero);
            int tempMaxRaw = (int)decimal.Round(tempMaxDisplay * 10m, 0, MidpointRounding.AwayFromZero);
            int tempRaw = data.StackTemperature;
            if (tempRaw >= tempMaxRaw)
            {
                duty = maxDuty;
            }
            else if (tempRaw <= tempMinRaw)
            {
                duty = minDuty;
            }
            else
            {
                double ratio = (double)(tempRaw - tempMinRaw) / (tempMaxRaw - tempMinRaw);
                duty = minDuty + (int)Math.Round((maxDuty - minDuty) * ratio,
                    MidpointRounding.AwayFromZero);
            }

            rpm = (int)Math.Round(maxRpm * duty / 100.0, MidpointRounding.AwayFromZero);
            return true;
        }

        /// <summary>
        /// 更新ViewModel中的压力参数（MVVM模式）
        /// </summary>
        private void UpdateViewModelPressureParams(PressureParams data)
        {
            if (_viewModel == null) return;
            
            _viewModel.HighPressureMin = data.HighPressureMin.ToString();
            _viewModel.HighPressureRed = data.HighPressureRed.ToString();
            _viewModel.HighPressureYellow = data.HighPressureYellow.ToString();
            _viewModel.HighPressureMax = data.HighPressureMax.ToString();
            _viewModel.LowPressureMin = data.LowPressureMin.ToString();
            _viewModel.LowPressureMax = data.LowPressureMax.ToString();
            _viewModel.HighPressureRange = data.HighPressureRange.ToString();
            _viewModel.LowPressureRange = data.LowPressureRange.ToString();

            // 已从设备取得当前值（含未展示的红/黄阈值），此后写入可免“覆盖未展示字段”确认
            _pressureParamsLoadedFromDevice = true;
        }

        /// <summary>
        /// 更新电堆信息显示（保留用于图表数据）
        /// </summary>
        private void UpdateStackInfo(StackInfoData data)
        {
            // 保存DCDC电压电流用于图表显示
            _currentInputVoltage = data.DCDCInputVoltage / 10.0;
            _currentInputCurrent = data.DCDCInputCurrent / 10.0;
            _currentOutputVoltage = data.DCDCOutputVoltage / 10.0;
            _currentOutputCurrent = data.DCDCOutputCurrent / 10.0;
            
            // UI更新已通过UpdateViewModelStackInfo在HandleStackInfoResponse中完成
        }

        // ❌ 已删除：UpdatePressureParams - 已被 UpdateViewModelPressureParams 替代

        /// <summary>
        /// 更新阀门参数显示
        /// </summary>
        /// <summary>
        /// 0x0A+0x0201 阀门参数回填：协议 0.1s 单位 ÷10 转为界面秒值。
        /// 字段映射（固件 fcu_protocol.c read_config 0x0201）：
        ///   start_first_duration  → 电堆吹扫①（开机时序区）
        ///   start_second_duration → 电堆吹扫②（开机时序区）
        ///   normal_interval/duration → 排水间隔/排水打开（正常运行排水区）
        ///   stop_duration → 隐藏框原值保留（固件死参数：可读写但控制逻辑未使用，写入时原样回传）
        /// </summary>
        private void UpdateValveParams(ValveParams data)
        {
            tbx_StartupDrainFirstOpen.Text = (data.FirstOpenTime / 10.0).ToString("F2");
            tbx_StartupDrainSecondOpen.Text = (data.SecondOpenTime / 10.0).ToString("F2");
            tbx_ValveNormalInterval.Text = (data.NormalInterval / 10.0).ToString("F2");
            tbx_ValveNormalOpen.Text = (data.NormalOpenTime / 10.0).ToString("F2");
            tbx_ValveShutdownOpen.Text = data.ShutdownOpenTime.ToString();
            // 0x0201 无 shutdown_empty，总时长按当前排空保持值重算（公式见 UpdateShutdownTotal）
            UpdateShutdownTotal();
        }

        /// <summary>
        /// 更新DCDC参数显示
        /// </summary>
        private void UpdateDCDCParams(DCDCParams data)
        {
            tbx_DCDCMinVoltage.Text = data.MinVoltage.ToString();
            tbx_DCDCStopVoltage.Text = data.StopVoltage.ToString();
        }

        /// <summary>
        /// 更新风扇温度参数显示
        /// </summary>
        private void UpdateFanTempParams(FanTempParams data)
        {
            tbx_FanLowDuty.Text = data.LowSpeedDuty.ToString();
            tbx_TempMin.Text = (data.TempMin / 10m).ToString("0.0", CultureInfo.CurrentCulture);
            tbx_TempMax.Text = (data.TempMax / 10m).ToString("0.0", CultureInfo.CurrentCulture);
            tbx_FanFullSpeedTime.Text = data.FullSpeedTime.ToString();
            tbx_LowTempCheckTime.Text = data.LowTempCheckTime.ToString();
            tbx_FanMinRpm.Text = data.FanMinRpm.ToString();
        }

        /// <summary>
        /// 更新工作流程参数显示 (0x0601)
        /// </summary>
        /// <summary>
        /// 0x0A+0x0601 工作流程参数回填：协议 0.1s 单位 ÷10 转为界面秒值。
        /// 字段映射（固件 fcu_valve.c 关机序列 open1→close→open2，客户模型首段"关闭①"为运行态延续，无协议字段）：
        ///   purge_h2_open1/close/open2 → 氢阀补气① / 关闭② / 补气②（关机时序区）
        ///   purge_drain_hold → 关机排水保持（关机流程参数区）
        ///   排氢关机时长 = 关机排水保持 + 固定排空 5.0s（FCU_VALVE_T_SHUTDOWN_EMPTY=50）
        ///   drain_mode 仅 0=定时（固件 fcu_protocol_set_flow_params 拒绝非0）
        /// </summary>
        private void UpdateFlowParams(FlowParams data)
        {
            tbx_ShutdownH2Open1.Text = (data.PurgeH2Open1 / 10.0).ToString("F2");
            tbx_ShutdownH2Close2.Text = (data.PurgeH2Close / 10.0).ToString("F2");
            tbx_ShutdownH2Open2.Text = (data.PurgeH2Open2 / 10.0).ToString("F2");
            tbx_PurgeDrainHoldSec.Text = (data.PurgeDrainHold / 10.0).ToString("F2");
            // 总时长 = 排水保持(本帧回填) + 排空保持(0x0202 已先回填或界面值)，公式见 UpdateShutdownTotal
            UpdateShutdownTotal();
            cbx_DrainMode.SelectedIndex = 0;
            tbx_LowNormalRef.Text = data.LowNormalRef.ToString();
            tbx_LoadDuty.Text = data.LoadDuty.ToString();
        }

        // ❌ 已删除：btn_ReadFlowParams_Click / btn_ReadValveParams_Click - 已并入 btn_ReadValvePageParams_Click（0x0201+0x0601 顺序读取）

        /// <summary>
        /// 写入关机流程参数（0x0E，数据域 12B：purge 三段 + 排水保持 + 模式 + 阈值 + 拉载风扇占空比）。
        /// 数据源为关机时序输入框的秒值，经 ParseSecondsTo0P1 换算。
        /// 固件 fcu_protocol_set_flow_params() 校验：四个 purge 时长非零、drain_mode 仅 0、
        /// 阈值非零、占空比≤100，命中则整帧不生效。
        /// </summary>
        private void btn_SetFlowParams_Click(object sender, RoutedEventArgs e)
        {
            SetParameter("关机流程参数(0x0E)", ProtocolConstants.FUNC_SET_FLOW_PARAMS, BuildFlowParamsDataFromUi);
        }

        private byte[] BuildFlowParamsDataFromUi()
        {
            var p = new FlowParams
            {
                PurgeH2Open1 = ParseSecondsTo0P1(tbx_ShutdownH2Open1, "氢阀补气①(purge_h2_open1)"),
                PurgeH2Close = ParseSecondsTo0P1(tbx_ShutdownH2Close2, "氢阀关闭②(purge_h2_close)"),
                PurgeH2Open2 = ParseSecondsTo0P1(tbx_ShutdownH2Open2, "氢阀补气②(purge_h2_open2)"),
                PurgeDrainHold = ParseSecondsTo0P1(tbx_PurgeDrainHoldSec, "关机排水保持(purge_drain_hold)"),
                DrainMode = 0,   // 固件仅接受定时模式（fcu_protocol_set_flow_params 拒绝非0），UI 也已限定
                LowNormalRef = ParseRequiredField(tbx_LowNormalRef, "低压反馈排水阈值(low_normal_ref)", 1, ushort.MaxValue, "kPa"),
                LoadDuty = (byte)ParseRequiredField(tbx_LoadDuty, "拉载风扇占空比(load_duty)", 0, 100, "%")
            };

            return ResponseParser.BuildFlowParamsData(p);
        }

        /// <summary>
        /// 更新通用参数显示
        /// </summary>
        private void UpdateGeneralParams(GeneralParams data)
        {
            tbx_ScanPeriod.Text = data.ScanPeriod.ToString();
        }

        /// <summary>
        /// 更新拉载时序参数显示
        /// </summary>
        private void UpdateLoadSequenceParams(LoadSequenceParams data)
        {
            try
            {
                int count = Math.Min((int)data.SequenceCount, 15);
                TextBox[] currentBoxes = {
                    tbx_Seq1_Current, tbx_Seq2_Current, tbx_Seq3_Current, tbx_Seq4_Current, tbx_Seq5_Current,
                    tbx_Seq6_Current, tbx_Seq7_Current, tbx_Seq8_Current, tbx_Seq9_Current, tbx_Seq10_Current,
                    tbx_Seq11_Current, tbx_Seq12_Current, tbx_Seq13_Current, tbx_Seq14_Current, tbx_Seq15_Current
                };
                TextBox[] timeBoxes = {
                    tbx_Seq1_Time, tbx_Seq2_Time, tbx_Seq3_Time, tbx_Seq4_Time, tbx_Seq5_Time,
                    tbx_Seq6_Time, tbx_Seq7_Time, tbx_Seq8_Time, tbx_Seq9_Time, tbx_Seq10_Time,
                    tbx_Seq11_Time, tbx_Seq12_Time, tbx_Seq13_Time, tbx_Seq14_Time, tbx_Seq15_Time
                };

                for (int i = 0; i < count; i++)
                {
                    currentBoxes[i].Text = (data.Currents[i] / 10m).ToString("0.0", CultureInfo.CurrentCulture);
                    timeBoxes[i].Text = (data.Times[i] / 100m).ToString("0.00", CultureInfo.CurrentCulture);
                }

                /* 超出 count 的行清空：固件默认时序为 14 条，回读 14 条设备后
                 * 第 15 行若残留旧值，用户点写入会按 15 条重发陈旧条目（固件静默接受） */
                for (int i = count; i < currentBoxes.Length; i++)
                {
                    currentBoxes[i].Text = string.Empty;
                    timeBoxes[i].Text = string.Empty;
                }
            }
            catch
            {
                // 静默处理
            }
        }

        /// <summary>
        /// 更新拉载控制参数显示
        /// </summary>
        private void UpdateLoadControlParams(LoadControlParams data)
        {
            try
            {
                tbx_LoadPeakCurrent.Text = (data.PeakCurrent / 10m).ToString("0.0", CultureInfo.CurrentCulture);
                tbx_LoadLockFactor.Text = (data.LockFactor / 1000m).ToString("0.000", CultureInfo.CurrentCulture);
                /* 透传缓存防污染: 固件校验合法域为 1..1000 (fcu_protocol.c 写入校验),
                 * 畸形回读帧(解析失败给 0)不得进缓存——否则后续 0x0502 整帧被固件拒绝, 四个有效字段也写不进 */
                if (data.LockFactor >= 1 && data.LockFactor <= 1000)
                {
                    _lastLockFactor0P001 = data.LockFactor;   /* 保留字段透传缓存随回读更新 */
                }
                chk_LoadLockFlag.IsChecked = data.LockFlag == 1;
                tbx_LoadMinVoltage.Text = (data.MinVoltage / 10m).ToString("0.0", CultureInfo.CurrentCulture);
                tbx_LoadCurrentStep.Text = (data.CurrentStep / 10m).ToString("0.0", CultureInfo.CurrentCulture);
            }
            catch
            {
                // 静默处理
            }
        }

        /// <summary>
        /// 更新拉载启动参数显示
        /// </summary>
        private void UpdateLoadStartParams(LoadStartParams data)
        {
            try
            {
                tbx_LoadFirstCount.Text = data.FirstCount.ToString();
                tbx_LoadFirstInterval.Text = data.FirstInterval.ToString();
                tbx_LoadNormalInterval.Text = data.NormalInterval.ToString();
            }
            catch
            {
                // 静默处理
            }
        }

        /// <summary>
        /// 处理拉载状态上报（功能码0xF2）
        /// </summary>
        private void HandleLoadStatusReport(byte[] frame)
        {
            LoadStatusData data = ResponseParser.ParseLoadStatusReport(frame);
            MarshalRxToUi(() =>
            {
                UpdateLoadStatusDisplay(data);
                UpdateStatus("收到拉载状态上报");
            });
        }

        /// <summary>
        /// 更新拉载状态实时显示
        /// </summary>
        private void UpdateLoadStatusDisplay(LoadStatusData data)
        {
            try
            {
                tbk_LoadState.Text = data.GetLoadStateName();
                tbk_LoadCurrentSequence.Text = data.CurrentSequence.ToString();
                tbk_LoadCurrentCurrent.Text = (data.CurrentCurrent / 10.0).ToString("F1");
                tbk_LoadTargetCurrent.Text = (data.TargetCurrent / 10.0).ToString("F1");
                tbk_LoadCurrentVoltage.Text = (data.CurrentVoltage / 10.0).ToString("F1");
                tbk_LoadCurrentPower.Text = (data.CurrentPower / 10.0).ToString("F1");
                tbk_LoadMaxPower.Text = (data.MaxPower / 10.0).ToString("F1");
                tbk_LoadLockCurrent.Text = (data.LockCurrent / 10.0).ToString("F1");

                // 根据拉载状态设置动态颜色
                Brush stateColor = data.LoadState switch
                {
                    0x01 => new SolidColorBrush(Color.FromRgb(76, 175, 80)),    // 进行中-绿色
                    0x02 => new SolidColorBrush(Color.FromRgb(33, 150, 243)),   // 完成-蓝色
                    0x03 => new SolidColorBrush(Color.FromRgb(255, 152, 0)),    // 暂停-橙色
                    0x04 => new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // 异常停止-红色
                    0x05 => new SolidColorBrush(Color.FromRgb(76, 175, 80)),    // 锁定成功-绿色
                    0x06 => new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // 锁定失败-红色
                    _ => new SolidColorBrush(Colors.Black)                       // 默认-黑色
                };
                tbk_LoadState.Foreground = stateColor;
            }
            catch
            {
                // 静默处理
            }
        }

        /// <summary>
        /// 显示报警消息。Growl 非模态通知 [审查修复 P3]: 原模态 MessageBox 在无人值守场景下
        /// 会冻结 UI 并使编组的界面回调排队, 报警流持续时弹窗需人工逐个关闭、长期占用界面。
        /// </summary>
        private void ShowAlarmMessage(AlarmData alarm)
        {
            string title = $"{alarm.GetAlarmLevelName()}报警";
            string message = $"报警类型：{alarm.GetAlarmTypeName()}\n报警数值：{alarm.GetAlarmValueDescription()}";

            switch (alarm.AlarmLevel)
            {
                case 0x03:
                case 0x04:
                    HC.Growl.Error($"{title}: {message}");
                    break;
                case 0x02:
                    HC.Growl.Warning($"{title}: {message}");
                    break;
                default:
                    HC.Growl.Info($"{title}: {message}");
                    break;
            }
            UpdateStatus($"收到报警：{alarm.GetAlarmTypeName()}");
        }

        /// <summary>
        /// 显示状态变更
        /// </summary>
        private void ShowStateChange(StateChangeData stateChange)
        {
            // 0x0101 电堆工作状态：下位机只在状态跳变时上报，是关闭轮询时唯一的实时状态通道，
            // 因此必须同步刷新主界面状态显示，否则会一直停留在上次 0x87 响应的旧值
            if (stateChange.StateType == 0x0101)
            {
                string oldName = GetStackStateName(stateChange.OldState);
                string newName = GetStackStateName(stateChange.NewState);

                if (_viewModel != null)
                {
                    _viewModel.StackState = newName;
                }

                UpdateStatus($"状态变更：{stateChange.GetStateTypeName()}: {oldName} → {newName}");
                return;
            }

            // 0x4842 RS485 通讯心跳(每5s一帧, 下位机 main.c)：链路保活信号，静默忽略防止刷屏；
            // 链路存活判断仍走 0x87 轮询命中机制
            if (stateChange.StateType == 0x4842)
            {
                return;
            }

            // 0x0105 低压泄压保护：仅显示状态转换（压力恢复上报 1→0），
            // 0x000C 泄压警示 Growl 不因此清除（下位机后台告警须人工消除）
            if (stateChange.StateType == 0x0105)
            {
                string reliefOld = GetLowPressureReliefStateName(stateChange.OldState);
                string reliefNew = GetLowPressureReliefStateName(stateChange.NewState);
                UpdateStatus($"状态变更：{stateChange.GetStateTypeName()}: {reliefOld} → {reliefNew}");
                return;
            }

            string message = $"{stateChange.GetStateTypeName()}: {stateChange.OldState} → {stateChange.NewState}";
            UpdateStatus($"状态变更：{message}");
        }

        // ❌ 已删除：旧 AA55-BMS 响应处理(0x82-32B/0x83/0x87-4B/0x90/0xF3/F4/F5)
        // BMS 已重写为 Modbus RTU 主站(BmsService), 不再走 AA55 帧分发 [上位机优化计划 D4]

#if ENABLE_LICENSE_FEATURE
        /// <summary>
        /// 更新许可证状态显示
        /// </summary>
        private void UpdateLicenseStatusDisplay()
        {
            try
            {
                if (_licenseService == null) return;

                var validationResult = _licenseService.ValidateLicense();

                string statusText;
                Brush statusColor;

                switch (validationResult.Status)
                {
                    case LicenseStatus.TrialActive:
                        statusText = $"试用版 (剩余 {validationResult.RemainingDays} 天)";
                        statusColor = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                        break;

                    case LicenseStatus.Activated:
                        if (validationResult.RemainingDays == -1)
                        {
                            statusText = "已激活 (永久授权)";
                        }
                        else
                        {
                            statusText = $"已激活 (剩余 {validationResult.RemainingDays} 天)";
                        }
                        statusColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                        break;

                    default:
                        statusText = "未激活";
                        statusColor = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                        break;
                }

                // 在状态栏显示许可证状态（通过ViewModel）
                if (_viewModel != null)
                {
                    _viewModel.StatusMessage = $"{_viewModel.StatusMessage} | {statusText}";
                }
            }
            catch (Exception ex)
            {
                // 静默处理错误，不影响主程序
                UpdateStatus($"许可证状态获取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 许可证管理按钮点击事件
        /// </summary>
        private void TbkLicense_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_licenseService == null)
                {
                    MessageBox.Show("许可证服务未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var licenseWindow = new Views.LicenseWindow();
                licenseWindow.ShowDialog();

                // 刷新许可证状态显示
                UpdateLicenseStatusDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开许可证管理窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

#endif

        #endregion

        #region BMS电池管理功能 (Modbus RTU, V1.28-S)

        /// <summary>
        /// 读取BMS按钮: 手动触发一次 03H 读 reg128~152
        /// </summary>
        private void btn_ReadBMS_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return;
            }
            if (!CheckBmsReady()) return;
            _bmsService?.ReadOnce();
        }

        /// <summary>
        /// 开启BMS自动刷新 (03H轮询)
        /// </summary>
        private void btn_StartBMSPolling_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return;
            }

            if (!CheckBmsReady()) return;

            double intervalSec = 2.0;
            if (cbx_BMSPollingInterval.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                intervalSec = int.Parse(item.Tag.ToString()!) / 1000.0;
            }

            _bmsService?.StartPolling(intervalSec);
            btn_StartBMSPolling.IsEnabled = false;
            btn_StopBMSPolling.IsEnabled = true;
            cbx_BMSPollingInterval.IsEnabled = false;
            UpdateStatus($"已开启BMS自动刷新 ({intervalSec:0.#}s)");
        }

        /// <summary>
        /// 停止BMS自动刷新
        /// </summary>
        private void btn_StopBMSPolling_Click(object sender, RoutedEventArgs e)
        {
            _bmsService?.StopPolling();
            btn_StartBMSPolling.IsEnabled = true;
            btn_StopBMSPolling.IsEnabled = false;
            cbx_BMSPollingInterval.IsEnabled = true;
            UpdateStatus("已停止BMS自动刷新");
        }

        /// <summary>
        /// BMS从站地址应用 (UI可配, 默认1, 与固件FCU_MODBUS_SLAVE_ADDR一致) [上位机优化计划 D2]
        /// </summary>
        private void btn_ApplyBmsAddress_Click(object sender, RoutedEventArgs e)
        {
            if (!byte.TryParse(tbx_BmsAddress.Text, out byte addr) || addr == 0)
            {
                HC.Growl.Warning("BMS地址必须为1-254");
                return;
            }
            if (_bmsService != null)
            {
                _bmsService.SlaveAddress = addr;
                UpdateStatus($"BMS从站地址已设为 {addr}");
            }
        }

        /// <summary>允许充电: 10H 写 reg1=0xAAAA 打开充电MOS [BMS协议 §3.3]</summary>
        private void btn_BMS_AllowCharge_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckBmsReady()) return;
            _bmsService?.SetChargeMos(true);
        }

        /// <summary>允许放电: 10H 写 reg2=0xAAAA 打开放电MOS</summary>
        private void btn_BMS_AllowDischarge_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckBmsReady()) return;
            _bmsService?.SetDischargeMos(true);
        }

        /// <summary>允许充放电: 先开充电MOS, 成功回调中再开放电MOS(避免总线忙冲突)</summary>
        private void btn_BMS_AllowBoth_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckBmsReady()) return;
            _pendingSecondMosWrite = () => _bmsService?.SetDischargeMos(true);
            _bmsService?.SetChargeMos(true);
        }

        /// <summary>禁止充放电: 先关充电MOS, 成功回调中再关放电MOS</summary>
        private void btn_BMS_Forbid_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckBmsReady()) return;
            _pendingSecondMosWrite = () => _bmsService?.SetDischargeMos(false);
            _bmsService?.SetChargeMos(false);
        }

        private bool CheckBmsReady()
        {
            if (_upgradePrecheckMode || !DirectBmsMode || !_bmsTakeoverConfirmed || _bmsReturnInFlight || _bmsBusExchangeInProgress || _standbyPeriod?.IsBusy == true || _currentCalibration?.IsBusy == true)
            {
                HC.Growl.Warning("请先选择直接Modbus诊断并等待FCU确认总线接管");
                return false;
            }
            if (!_isSerialOpen)
            {
                HC.Growl.Warning("请先打开串口");
                return false;
            }
            return true;
        }

        /// <summary>排队的第二次MOS写 (允许/禁止充放电需两帧)</summary>
        private Action? _pendingSecondMosWrite;

        /// <summary>
        /// MOS写完成: 反馈结果; 首次成功且有排队第二次写则继续
        /// </summary>
        private void OnBmsMosWriteCompleted(bool success, string message)
        {
            if (success)
            {
                UpdateStatus(message);
            }
            else
            {
                HC.Growl.Error(message);
                UpdateStatus(message);
            }

            var pending = _pendingSecondMosWrite;
            _pendingSecondMosWrite = null;
            if (success && pending != null)
            {
                pending();
            }
        }

        /// <summary>
        /// BMS数据更新 (03H reg128~152) [上位机优化计划 D8]
        /// 无对应寄存器的字段显示"--"
        /// </summary>
        private void OnBmsDataUpdated(BmsData data)
        {
            if (!DirectBmsMode) return;
            if (!data.Online)
            {
                ResetBmsDisplay();
                return;
            }

            // SOC 条件着色：使用最近一次 0x0701 回读值，未回读前使用固件默认值。
            tbk_BMS_SOC.Text = data.Soc.ToString();
            tbk_BMS_SOC.Foreground = data.Soc > _socStartMax
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36))   // 红: 拒绝启动
                : data.Soc > _socStopHigh
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)) // 橙: 运行中会自动停机
                    : new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
            tbk_OverviewBmsSoc.Text = data.Soc.ToString();
            tbk_OverviewBmsSoc.Foreground = tbk_BMS_SOC.Foreground;
            tbk_BMS_SOH.Text = data.Soh.ToString();
            tbk_BMS_RemainCapacity.Text = data.RemainCapacity.ToString("F2");
            tbk_BMS_RatedCapacity.Text = data.RatedCapacity.ToString("F2");
            tbk_BMS_TotalVoltage.Text = data.PackVoltage.ToString("F2");
            tbk_BMS_TotalCurrent.Text = data.Current.ToString("F2");
            tbk_BMS_MaxCellVoltage.Text = data.CellVoltMax.ToString("F3");
            tbk_BMS_MinCellVoltage.Text = data.CellVoltMin.ToString("F3");
            // 平均单体电压/平均温度: 无对应寄存器
            tbk_BMS_AvgCellVoltage.Text = "--";
            tbk_BMS_MaxTemp.Text = data.CellTempMax.ToString("F1");
            tbk_BMS_MinTemp.Text = data.CellTempMin.ToString("F1");
            tbk_BMS_AvgTemp.Text = "--";
            tbk_BMS_WorkState.Text = GetBmsWorkStateText(data.SystemStatus);
            tbk_BMS_ChargeDischargeState.Text =
                $"充电MOS:{(data.ChargeMosOn ? "开" : "关")} 放电MOS:{(data.DischargeMosOn ? "开" : "关")}";
            tbk_BMS_CycleCount.Text = data.CycleCount.ToString();
            tbk_BMS_FaultCode.Text = data.FaultStatus == 0 ? "无故障" : $"0x{data.FaultStatus:X4}";
            tbk_BMS_SeriesCells.Text = data.SeriesCells.ToString();

            // 故障信息区: reg137告警/138保护/139故障 位解码 [BMS协议 附表1/2/3]
            // 保护位中带「⚠」的会阻断 FCU 拉载 (对应固件 FCU_BMS_PROTECT_CRITICAL_MASK)
            tbx_BMS_CurrentFault.Text =
                $"告警: {BmsBitDecoder.DecodeAlarm(data.AlarmStatus)}\n" +
                $"保护: {BmsBitDecoder.DecodeProtect(data.ProtectStatus)}\n" +
                $"故障: {BmsBitDecoder.DecodeFault(data.FaultStatus)}";
            // 历史记录需读reg2400+, 本期不支持
            tbx_BMS_HistoryFault.Text = "--";

            UpdateLoadBlockBanner(data);
        }

        /// <summary>
        /// 刷新电流设置 Tab 顶部的 BMS / SOC 运行限制提示条。
        /// 下位机 fcu_control_start_load() 等入口在 BMS 阻断时是静默 return，
        /// 0x82 响应仍回 0x00 成功，上位机会显示「设置成功」而拉载实际没启动，
        /// 因此必须把阻断状态与原因主动呈现出来。
        /// </summary>
        private void UpdateLoadBlockBanner(BmsData data)
        {
            if (DirectBmsMode)
            {
                tbk_LoadBlockReason.Text = "直接Modbus诊断暂停了FCU自身BMS采集，FCU侧SOC门控与BMS故障耦合不可用。退出诊断并确认归还轮询后恢复；直读数据不代表FCU正在执行门控。";
                bd_LoadBlockBanner.Visibility = Visibility.Visible;
                return;
            }
            var reasons = new List<string>();

            if (BmsBitDecoder.IsLoadBlocked(data.ProtectStatus, data.SystemStatus, out string bmsReason))
            {
                reasons.Add($"BMS保护阻断设流：{bmsReason}");
            }

            if (data.Soc > _socStartMax)
            {
                reasons.Add($"SOC={data.Soc}% > {_socStartMax}%：电堆启动会被拒绝（不是拉载指令阻断）");
            }
            if (data.Soc > _socStopHigh)
            {
                reasons.Add($"SOC={data.Soc}% > {_socStopHigh}%：电堆运行中会自动停机");
            }

            if (reasons.Count > 0)
            {
                tbk_LoadBlockReason.Text = "⚠ BMS / SOC 运行限制 — " + string.Join("; ", reasons);
                bd_LoadBlockBanner.Visibility = Visibility.Visible;
            }
            else
            {
                bd_LoadBlockBanner.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// BMS工作状态文本 [BMS协议 附表4]: BIT8充电指示/BIT9放电指示/BIT10充满/BIT11待机
        /// </summary>
        private static string GetBmsWorkStateText(ushort sys)
        {
            if ((sys & 0x0100) != 0) return "充电中";
            if ((sys & 0x0200) != 0) return "放电中";
            if ((sys & 0x0400) != 0) return "已充满";
            if ((sys & 0x0800) != 0) return "待机";
            return $"未知(0x{sys:X4})";
        }

        /// <summary>
        /// BMS显示复位 (离线/断开串口时全部置"--")
        /// </summary>
        private void ResetBmsDisplay()
        {
            tbk_BMS_SOC.Text = "--";
            tbk_BMS_SOC.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
            tbk_OverviewBmsSoc.Text = "--";
            tbk_OverviewBmsSoc.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
            tbk_BMS_SOH.Text = "--";
            tbk_BMS_RemainCapacity.Text = "--";
            tbk_BMS_RatedCapacity.Text = "--";
            tbk_BMS_TotalVoltage.Text = "--";
            tbk_BMS_TotalCurrent.Text = "--";
            tbk_BMS_MaxCellVoltage.Text = "--";
            tbk_BMS_MinCellVoltage.Text = "--";
            tbk_BMS_AvgCellVoltage.Text = "--";
            tbk_BMS_MaxTemp.Text = "--";
            tbk_BMS_MinTemp.Text = "--";
            tbk_BMS_AvgTemp.Text = "--";
            tbk_BMS_WorkState.Text = "--";
            tbk_BMS_ChargeDischargeState.Text = "--";
            tbk_BMS_CycleCount.Text = "--";
            tbk_BMS_FaultCode.Text = "--";
            tbk_BMS_SeriesCells.Text = "--";
            tbx_BMS_CurrentFault.Text = "--";
            tbx_BMS_HistoryFault.Text = "--";

            // BMS 数据不可用时，下位机侧的 SOC 门控与 BMS 故障耦合同样处于失效状态
            // (fcu_control_bms_couple() 在 !online 时清零 s_bms_load_block)，这是安全相关的降级
            tbk_LoadBlockReason.Text =
                "⚠ BMS 数据不可用 — 下位机侧 SOC 门控与 BMS 故障耦合已失效，"
                + "电堆可在 SOC 过高或 BMS 保护状态下被启动";
            bd_LoadBlockBanner.Visibility = Visibility.Visible;
        }

        #endregion

        #region 窗口事件

        private void btn_CopyCommunicationLog_Click(object sender, RoutedEventArgs e)
        {
            if (lbx_CommunicationLog.SelectedItems.Count == 0)
            {
                HC.Growl.Warning("请先选择要复制的通信日志");
                return;
            }

            try
            {
                var builder = new StringBuilder();
                foreach (object item in lbx_CommunicationLog.SelectedItems)
                {
                    if (item is CommunicationLogEntry entry)
                    {
                        builder.AppendLine(entry.DisplayText);
                    }
                }

                Clipboard.SetText(builder.ToString().TrimEnd());
                UpdateStatus($"已复制 {lbx_CommunicationLog.SelectedItems.Count} 条通信日志");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"复制通信日志失败: {ex.Message}");
            }
        }

        private void btn_SaveCommunicationLog_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _viewModel.CommunicationLogs.Count == 0)
            {
                HC.Growl.Warning("当前没有可保存的通信日志");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "保存通信日志",
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".log",
                AddExtension = true,
                FileName = $"FCU通信日志_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var lines = new string[_viewModel.CommunicationLogs.Count];
                for (int i = 0; i < _viewModel.CommunicationLogs.Count; i++)
                {
                    lines[i] = _viewModel.CommunicationLogs[i].DisplayText;
                }

                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(false));
                UpdateStatus($"通信日志已保存: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"保存通信日志失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_timer != null)
            {
                _timer.Stop();
            }
            if (_chartUpdateTimer != null)
            {
                _chartUpdateTimer.Stop();
            }
            if (_socResponseTimeoutTimer != null)
            {
                _socResponseTimeoutTimer.Stop();
                _socResponseTimeoutTimer.Tick -= SocResponseTimeoutTimer_Tick;
            }
            if (_fanManualResponseTimeoutTimer != null)
            {
                _fanManualResponseTimeoutTimer.Stop();
                _fanManualResponseTimeoutTimer.Tick -= FanManualResponseTimeoutTimer_Tick;
            }
            if (_loadManualResponseTimeoutTimer != null)
            {
                _loadManualResponseTimeoutTimer.Stop();
                _loadManualResponseTimeoutTimer.Tick -= LoadManualResponseTimeoutTimer_Tick;
            }
            // [轮询修复] 诊断探针定时器 (Tick 为 lambda, Stop 即可)
            StopStackInfoLinkDiagnosis();

            if (_viewModel != null)
            {
                var serialPortManager = _viewModel.GetSerialPortManager();
                serialPortManager.DataReceived -= SerialPort_DataReceived;
                serialPortManager.ConnectionStateChanged -= OnSerialConnectionChanged;
                serialPortManager.FrameSent -= OnFrameSent;
            }

            // 串口清理由SerialPortManager在Dispose时处理
            _viewModel?.GetSerialPortManager()?.Dispose();
        }

        #endregion

        #region 拉载配方管理

        /// <summary>
        /// 加载配方列表到ListBox
        /// </summary>
        private void LoadRecipeList()
        {
            try
            {
                if (_recipeService == null) return;
                lbx_RecipeList.ItemsSource = _recipeService.GetAllRecipes();
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"加载配方列表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 配方选中事件：填充右侧参数编辑区
        /// </summary>
        private void lbx_RecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (lbx_RecipeList.SelectedItem is not LoadRecipe recipe) return;

                _selectedRecipeId = recipe.Id;
                tbk_CurrentRecipeName.Text = recipe.IsSystemPreset
                    ? $"[系统预设] {recipe.Name}"
                    : recipe.Name;

                // 填充30个TextBox
                TextBox[] currentBoxes = {
                    tbx_RecipeSeq1_Current, tbx_RecipeSeq2_Current, tbx_RecipeSeq3_Current,
                    tbx_RecipeSeq4_Current, tbx_RecipeSeq5_Current, tbx_RecipeSeq6_Current,
                    tbx_RecipeSeq7_Current, tbx_RecipeSeq8_Current, tbx_RecipeSeq9_Current,
                    tbx_RecipeSeq10_Current, tbx_RecipeSeq11_Current, tbx_RecipeSeq12_Current,
                    tbx_RecipeSeq13_Current, tbx_RecipeSeq14_Current, tbx_RecipeSeq15_Current
                };
                TextBox[] timeBoxes = {
                    tbx_RecipeSeq1_Time, tbx_RecipeSeq2_Time, tbx_RecipeSeq3_Time,
                    tbx_RecipeSeq4_Time, tbx_RecipeSeq5_Time, tbx_RecipeSeq6_Time,
                    tbx_RecipeSeq7_Time, tbx_RecipeSeq8_Time, tbx_RecipeSeq9_Time,
                    tbx_RecipeSeq10_Time, tbx_RecipeSeq11_Time, tbx_RecipeSeq12_Time,
                    tbx_RecipeSeq13_Time, tbx_RecipeSeq14_Time, tbx_RecipeSeq15_Time
                };

                int count = Math.Min((int)recipe.SequenceCount, 15);
                for (int i = 0; i < 15; i++)
                {
                    if (i < count)
                    {
                        currentBoxes[i].Text = recipe.Currents[i].ToString();
                        timeBoxes[i].Text = recipe.Times[i].ToString();
                    }
                    else
                    {
                        currentBoxes[i].Text = "0";
                        timeBoxes[i].Text = "0";
                    }
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"加载配方参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 新建配方
        /// </summary>
        private void btn_NewRecipe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Views.RecipeNameDialog("请输入配方名称");
                dialog.Owner = this;
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    var (success, message, recipe) = _recipeService!.AddRecipe(dialog.InputText);
                    if (success && recipe != null)
                    {
                        // 将当前编辑区的值填入新配方
                        var (currents, times) = ReadRecipeTextBoxes();
                        _recipeService.SaveRecipe(recipe.Id, currents, times);
                        LoadRecipeList();
                        lbx_RecipeList.SelectedItem = recipe;
                        HC.Growl.Success("配方创建成功");
                    }
                    else
                    {
                        HC.Growl.Warning(message);
                    }
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"新建配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 复制当前选中的配方
        /// </summary>
        private void btn_CopyRecipe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedRecipeId))
                {
                    HC.Growl.Warning("请先选择一个配方");
                    return;
                }

                var (success, message, recipe) = _recipeService!.CopyRecipe(_selectedRecipeId);
                if (success && recipe != null)
                {
                    LoadRecipeList();
                    lbx_RecipeList.SelectedItem = recipe;
                    HC.Growl.Success("配方复制成功");
                }
                else
                {
                    HC.Growl.Warning(message);
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"复制配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重命名用户配方
        /// </summary>
        private void btn_RenameRecipe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedRecipeId))
                {
                    HC.Growl.Warning("请先选择一个配方");
                    return;
                }

                var recipe = _recipeService!.GetRecipeById(_selectedRecipeId);
                if (recipe == null) return;

                if (recipe.IsSystemPreset)
                {
                    HC.Growl.Warning("系统预设配方不可重命名");
                    return;
                }

                var dialog = new Views.RecipeNameDialog("请输入新名称", recipe.Name);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    var (success, message) = _recipeService.RenameRecipe(_selectedRecipeId, dialog.InputText);
                    if (success)
                    {
                        LoadRecipeList();
                        tbk_CurrentRecipeName.Text = dialog.InputText;
                        HC.Growl.Success("重命名成功");
                    }
                    else
                    {
                        HC.Growl.Warning(message);
                    }
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"重命名失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除用户配方
        /// </summary>
        private void btn_DeleteRecipe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedRecipeId))
                {
                    HC.Growl.Warning("请先选择一个配方");
                    return;
                }

                var recipe = _recipeService!.GetRecipeById(_selectedRecipeId);
                if (recipe == null) return;

                if (recipe.IsSystemPreset)
                {
                    HC.Growl.Warning("系统预设配方不可删除");
                    return;
                }

                var result = MessageBox.Show($"确定要删除配方 \"{recipe.Name}\" 吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var (success, message) = _recipeService.DeleteRecipe(_selectedRecipeId);
                    if (success)
                    {
                        _selectedRecipeId = null;
                        tbk_CurrentRecipeName.Text = "请选择一个配方";
                        ClearRecipeTextBoxes();
                        LoadRecipeList();
                        HC.Growl.Success("配方已删除");
                    }
                    else
                    {
                        HC.Growl.Warning(message);
                    }
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"删除配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存当前编辑到选中的用户配方
        /// </summary>
        private void btn_SaveRecipe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedRecipeId))
                {
                    HC.Growl.Warning("请先选择一个配方");
                    return;
                }

                var recipe = _recipeService!.GetRecipeById(_selectedRecipeId);
                if (recipe == null) return;

                if (recipe.IsSystemPreset)
                {
                    HC.Growl.Warning("系统预设配方不可修改，请先复制为新配方再编辑");
                    return;
                }

                var (currents, times) = ReadRecipeTextBoxes();
                var (success, message) = _recipeService.SaveRecipe(_selectedRecipeId, currents, times);
                if (success)
                {
                    HC.Growl.Success("配方已保存");
                }
                else
                {
                    HC.Growl.Warning(message);
                }
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"保存配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从设备读取拉载时序参数到编辑区
        /// </summary>
        private void btn_ReadRecipeFromDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());
                SendReadConfigCommand(deviceAddress,
                    (byte)(ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE >> 8),
                    (byte)(ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE & 0xFF));
                UpdateStatus("已发送读取拉载时序参数命令（配方Tab）");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将当前编辑的配方参数应用到设备
        /// </summary>
        private void btn_ApplyRecipeToDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                /* 0x0504 在途期间互斥其他 0x04 类发送 (0x82 无子命令回显, 防误路由) */
                if (_pendingLoadManualCmd != null)
                {
                    HC.Growl.Warning($"拉载指令[{_pendingLoadManualCmd}]仍在等待FCU响应，暂不应用配方");
                    return;
                }

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                TextBox[] currentBoxes = {
                    tbx_RecipeSeq1_Current, tbx_RecipeSeq2_Current, tbx_RecipeSeq3_Current,
                    tbx_RecipeSeq4_Current, tbx_RecipeSeq5_Current, tbx_RecipeSeq6_Current,
                    tbx_RecipeSeq7_Current, tbx_RecipeSeq8_Current, tbx_RecipeSeq9_Current,
                    tbx_RecipeSeq10_Current, tbx_RecipeSeq11_Current, tbx_RecipeSeq12_Current,
                    tbx_RecipeSeq13_Current, tbx_RecipeSeq14_Current, tbx_RecipeSeq15_Current
                };
                TextBox[] timeBoxes = {
                    tbx_RecipeSeq1_Time, tbx_RecipeSeq2_Time, tbx_RecipeSeq3_Time,
                    tbx_RecipeSeq4_Time, tbx_RecipeSeq5_Time, tbx_RecipeSeq6_Time,
                    tbx_RecipeSeq7_Time, tbx_RecipeSeq8_Time, tbx_RecipeSeq9_Time,
                    tbx_RecipeSeq10_Time, tbx_RecipeSeq11_Time, tbx_RecipeSeq12_Time,
                    tbx_RecipeSeq13_Time, tbx_RecipeSeq14_Time, tbx_RecipeSeq15_Time
                };

                // 第 15 条留空（默认值为 0/空）→ count=14，对齐固件 V1.2 默认 14 条
                if (!TryParseLoadSequences(currentBoxes, timeBoxes,
                        out int seqCount, out ushort[] currents, out ushort[] times))
                {
                    return;
                }

                // count 个时序，每个时序 4 字节（电流 2 字节 + 时间 2 字节）
                byte[] data = new byte[2 + 1 + seqCount * 4];
                data[0] = 0x05; // 参数类型ID 高字节
                data[1] = 0x01; // 参数类型ID 低字节
                data[2] = (byte)seqCount;   // 时序总数（14 或 15）

                for (int i = 0; i < seqCount; i++)
                {
                    data[3 + i * 4] = (byte)(currents[i] >> 8);
                    data[4 + i * 4] = (byte)(currents[i] & 0xFF);
                    data[5 + i * 4] = (byte)(times[i] >> 8);
                    data[6 + i * 4] = (byte)(times[i] & 0xFF);
                }

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);
                SendFrame(frame);
                UpdateStatus("已发送应用配方到设备命令");
                HC.Growl.Success("配方已发送到设备");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"应用配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取配方编辑区30个TextBox的值
        /// </summary>
        private (ushort[] currents, ushort[] times) ReadRecipeTextBoxes()
        {
            var currents = new ushort[15];
            var times = new ushort[15];

            TextBox[] currentBoxes = {
                tbx_RecipeSeq1_Current, tbx_RecipeSeq2_Current, tbx_RecipeSeq3_Current,
                tbx_RecipeSeq4_Current, tbx_RecipeSeq5_Current, tbx_RecipeSeq6_Current,
                tbx_RecipeSeq7_Current, tbx_RecipeSeq8_Current, tbx_RecipeSeq9_Current,
                tbx_RecipeSeq10_Current, tbx_RecipeSeq11_Current, tbx_RecipeSeq12_Current,
                tbx_RecipeSeq13_Current, tbx_RecipeSeq14_Current, tbx_RecipeSeq15_Current
            };
            TextBox[] timeBoxes = {
                tbx_RecipeSeq1_Time, tbx_RecipeSeq2_Time, tbx_RecipeSeq3_Time,
                tbx_RecipeSeq4_Time, tbx_RecipeSeq5_Time, tbx_RecipeSeq6_Time,
                tbx_RecipeSeq7_Time, tbx_RecipeSeq8_Time, tbx_RecipeSeq9_Time,
                tbx_RecipeSeq10_Time, tbx_RecipeSeq11_Time, tbx_RecipeSeq12_Time,
                tbx_RecipeSeq13_Time, tbx_RecipeSeq14_Time, tbx_RecipeSeq15_Time
            };

            for (int i = 0; i < 15; i++)
            {
                if (!ushort.TryParse(currentBoxes[i].Text, out currents[i])) currents[i] = 0;
                if (!ushort.TryParse(timeBoxes[i].Text, out times[i])) times[i] = 0;
            }

            return (currents, times);
        }

        /// <summary>
        /// 清空配方编辑区所有TextBox
        /// </summary>
        private void ClearRecipeTextBoxes()
        {
            TextBox[] currentBoxes = {
                tbx_RecipeSeq1_Current, tbx_RecipeSeq2_Current, tbx_RecipeSeq3_Current,
                tbx_RecipeSeq4_Current, tbx_RecipeSeq5_Current, tbx_RecipeSeq6_Current,
                tbx_RecipeSeq7_Current, tbx_RecipeSeq8_Current, tbx_RecipeSeq9_Current,
                tbx_RecipeSeq10_Current, tbx_RecipeSeq11_Current, tbx_RecipeSeq12_Current,
                tbx_RecipeSeq13_Current, tbx_RecipeSeq14_Current, tbx_RecipeSeq15_Current
            };
            TextBox[] timeBoxes = {
                tbx_RecipeSeq1_Time, tbx_RecipeSeq2_Time, tbx_RecipeSeq3_Time,
                tbx_RecipeSeq4_Time, tbx_RecipeSeq5_Time, tbx_RecipeSeq6_Time,
                tbx_RecipeSeq7_Time, tbx_RecipeSeq8_Time, tbx_RecipeSeq9_Time,
                tbx_RecipeSeq10_Time, tbx_RecipeSeq11_Time, tbx_RecipeSeq12_Time,
                tbx_RecipeSeq13_Time, tbx_RecipeSeq14_Time, tbx_RecipeSeq15_Time
            };

            for (int i = 0; i < 15; i++)
            {
                currentBoxes[i].Text = "0";
                timeBoxes[i].Text = "0";
            }
        }

        /// <summary>
        /// 将设备响应的时序参数更新到配方编辑区
        /// </summary>
        public void UpdateRecipeTextBoxes(LoadSequenceParams data)
        {
            try
            {
                int count = Math.Min((int)data.SequenceCount, 15);
                TextBox[] currentBoxes = {
                    tbx_RecipeSeq1_Current, tbx_RecipeSeq2_Current, tbx_RecipeSeq3_Current,
                    tbx_RecipeSeq4_Current, tbx_RecipeSeq5_Current, tbx_RecipeSeq6_Current,
                    tbx_RecipeSeq7_Current, tbx_RecipeSeq8_Current, tbx_RecipeSeq9_Current,
                    tbx_RecipeSeq10_Current, tbx_RecipeSeq11_Current, tbx_RecipeSeq12_Current,
                    tbx_RecipeSeq13_Current, tbx_RecipeSeq14_Current, tbx_RecipeSeq15_Current
                };
                TextBox[] timeBoxes = {
                    tbx_RecipeSeq1_Time, tbx_RecipeSeq2_Time, tbx_RecipeSeq3_Time,
                    tbx_RecipeSeq4_Time, tbx_RecipeSeq5_Time, tbx_RecipeSeq6_Time,
                    tbx_RecipeSeq7_Time, tbx_RecipeSeq8_Time, tbx_RecipeSeq9_Time,
                    tbx_RecipeSeq10_Time, tbx_RecipeSeq11_Time, tbx_RecipeSeq12_Time,
                    tbx_RecipeSeq13_Time, tbx_RecipeSeq14_Time, tbx_RecipeSeq15_Time
                };

                for (int i = 0; i < count; i++)
                {
                    currentBoxes[i].Text = data.Currents[i].ToString();
                    timeBoxes[i].Text = data.Times[i].ToString();
                }

                /* 超出 count 的行回归"0"未用行约定（与主页面清空逻辑对称,
                 * 防止先选 15 条配方再读 14 条设备时第 15 行残留陈旧值） */
                for (int i = count; i < currentBoxes.Length; i++)
                {
                    currentBoxes[i].Text = "0";
                    timeBoxes[i].Text = "0";
                }
            }
            catch
            {
                // 静默处理
            }
        }

        #endregion

        #region 固件升级宿主桥接 [BL设计 §4.2]

        /// <summary>
        /// 升级页宿主桥接实现: 串口开关/AA55 直发/升级模式互斥/统一退出归一化
        /// </summary>
        private sealed class BlHostBridgeImpl : Views.FirmwareUpgradeView.IBlHostBridge
        {
            private readonly MainWindow _owner;

            public BlHostBridgeImpl(MainWindow owner) => _owner = owner;

            public event Action<byte[]>? AppFrameReceived
            {
                add => _owner.BlAppFrameReceived += value;
                remove => _owner.BlAppFrameReceived -= value;
            }

            public bool IsSerialOpen =>
                _owner._viewModel?.GetSerialPortManager().IsOpen == true;

            public string? PortName =>
                _owner._viewModel?.GetSerialPortManager().PortName;

            public byte DeviceAddress
            {
                get
                {
                    /* GetDeviceAddress 读取 UI 控件, 须在 UI 线程执行 (本属性被后台任务调用) */
                    return _owner.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (byte.TryParse(_owner.GetDeviceAddress(), out byte addr) && addr >= 1)
                                return addr;
                        }
                        catch { /* 界面地址非法时回退默认 1 */ }
                        return (byte)0x01;
                    });
                }
            }

            /// <summary>关闭并以新波特率重开串口 (UI 线程执行, 含重试) [BL设计 §4.2.4]</summary>
            public bool ReopenSerial(int baudRate)
            {
                bool ok = false;
                _owner.Dispatcher.Invoke(() =>
                {
                    var spm = _owner._viewModel?.GetSerialPortManager();
                    if (spm == null) return;

                    string? port = spm.PortName;
                    if (string.IsNullOrEmpty(port)) return;

                    if (spm.IsOpen) spm.Close();

                    /* 部分 USB 串口驱动 Close 后立即重开会失败, 100~300ms 间隔重试 [BL设计 §4.2.4(c)] */
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        ok = spm.Open(port, baudRate);
                        if (!ok) System.Threading.Thread.Sleep(200);
                    }

                    /* 重开失败时清除 en=0 抑制标志, 防止残留吞掉用户下一次手动连接的
                     * 总线接管 (触发条件: S8/退出/S1自救 的 9600 重开失败) [终审2 残留P2] */
                    if (!ok)
                    {
                        _owner._suppressNextAutoBmsPoll = false;
                    }
                });
                return ok;
            }

            public bool SendAppFrame(byte[] frame)
            {
                if (_owner._blUpgradeMode) return false;
                return _owner.Dispatcher.Invoke(() => _owner.SendAppFrameRaw(frame));
            }

            /// <summary>进入/退出升级模式: 标志 + 轮询挂起 + 主 Tab 互斥 + 协议状态机复位 [BL设计 §4.2]</summary>
            public bool TryPrepareUpgrade()
            {
                return _owner.Dispatcher.Invoke(() =>
                {
                    if (!_owner._isSerialOpen || _owner._standbyPeriod?.IsBusy == true || _owner._currentCalibration?.IsBusy == true ||
                        _owner._bmsCache?.Waiting == true || _owner._awaitingStackInfo || _owner._bmsService?.IsBusy == true ||
                        _owner._bmsBusExchangeInProgress || _owner._bmsReturnInFlight ||
                        _owner._pendingLoadManualCmd != null || _owner._pendingValveTimingSet.HasValue ||
                        _owner._pendingSocWrite != null || _owner._pendingSocReadDeviceAddress.HasValue ||
                        _owner._pendingSecondMosWrite != null || _owner._diagProbePending || _owner._pendingFanManualEnable.HasValue ||
                        Environment.TickCount64 - _owner._lastSharedSetAt < 3000) return false;
                    _owner._upgradePrecheckMode = true;
                    _owner._pollingTimer?.Stop();
                    _owner._bmsService?.StopPolling();
                    _owner.ResetFirmwareMainSession();
                    SetMainTabsEnabled(false);
                    return true;
                });
            }

            public void SetBlMode(bool on)
            {
                _owner.Dispatcher.Invoke(() =>
                {
                    _owner._blUpgradeMode = on;
                    if (!on) _owner._upgradePrecheckMode = _owner.FirmwareUpgradeView.IsBusy;

                    if (on)
                    {
                        /* 显式挂起电堆轮询与 BMS 轮询 (不依赖 Close/Open 副作用链)。
                         * 同时复位轮询在途标志 [审查修复 P2/B审查建议]: 防升级前最后一帧
                         * 0x07 未获响应把 BMS 仲裁条件卡在 true (纵深防御, 正常退出路径
                         * 经 ReopenSerial→Close→断开分支已有复位兜底) */
                        _owner._pollingTimer?.Stop();
                        _owner._bmsService?.StopPolling();
                        _owner.ResetFirmwareMainSession();
                        _owner._awaitingStackInfo = false;
                        _owner._stackInfoMissCount = 0;
                        _owner._diagProbePending = false;
                        _owner._protocolHandler?.Reset();
                        SetMainTabsEnabled(false);
                    }
                    else
                    {
                        _owner._protocolHandler?.Reset();
                        SetMainTabsEnabled(!_owner.FirmwareUpgradeView.IsBusy);
                        /* [BL升级 终审P1] 随后的 9600 重开不自动下发 en=0 (由正常断开链路统一归还) */
                        _owner._suppressNextAutoBmsPoll = _owner.FirmwareUpgradeView.IsBusy;
                        /* 轮询不自动恢复; 但按钮状态按实际运行态派生恢复:
                         * 升级入口可能停在"电堆轮询运行中"(前置检查失败路径轮询从未停),
                         * 一律恢复停止态会让用户无法直接停表 [审查修复 C审查建议] */
                        bool stackPollingAlive = _owner._pollingTimer?.IsEnabled == true;
                        bool bmsPollingAlive = _owner._bmsService?.IsPolling == true;
                        try
                        {
                            _owner.btn_StartPolling.IsEnabled = !stackPollingAlive;
                            _owner.btn_StopPolling.IsEnabled = stackPollingAlive;
                            _owner.cbx_PollingInterval.IsEnabled = !stackPollingAlive;
                            _owner.btn_StartBMSPolling.IsEnabled = !bmsPollingAlive;
                            _owner.btn_StopBMSPolling.IsEnabled = bmsPollingAlive;
                            _owner.cbx_BMSPollingInterval.IsEnabled = !bmsPollingAlive;
                        }
                        catch { }
                    }
                });
            }

            /// <summary>升级期间禁用除固件升级页外的全部主 Tab [BL设计 §4.2 集成点2 等效实现]</summary>
            private void SetMainTabsEnabled(bool enabled)
            {
                _owner.pnl_SerialActions.IsEnabled = enabled; // 包含串口/地址与快捷操作，防升级中切换目标。
                foreach (var item in _owner.MainTabControl.Items.OfType<System.Windows.Controls.TabItem>())
                {
                    item.IsEnabled = enabled;
                }
                if (_owner._firmwareUpgradeTab != null)
                {
                    _owner._firmwareUpgradeTab.IsEnabled = true;    /* 升级页自身保持可用 */
                    if (!enabled)
                    {
                        _owner.MainTabControl.SelectedItem = _owner._firmwareUpgradeTab;
                    }
                }
            }

            /// <summary>统一退出归一化 [BL设计 §4.4 退出规则]:
            /// APP 存活 → 走正常断开命令 (BeforeSerialClose 链路发 0x0D en=1 归还 BMS 总线);
            /// APP 不存活(BL 常驻) → 直接关串口, 提示重新升级。</summary>
            public bool NormalizeExit(bool appAlive)
            {
                return _owner.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (appAlive)
                        {
                            _owner._viewModel?.CloseSerialCommand?.Execute(null);
                        }
                        else
                        {
                            _owner._viewModel?.GetSerialPortManager().Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        _owner.UpdateStatus($"升级退出处理异常: {ex.Message}");
                        return false;
                    }
                    return !_owner._bmsBusMayBeOwned;
                });
            }
        }

        #endregion

    }
}
