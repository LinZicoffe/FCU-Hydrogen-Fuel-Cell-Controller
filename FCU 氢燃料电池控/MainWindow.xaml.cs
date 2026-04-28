// ======================================================================
// ⚠️ 功率曲线测试模式开关
// ======================================================================
// 启用测试模式：取消下面注释（删除前面的 //）
// 禁用测试模式：保持注释状态
// ======================================================================
//#define ENABLE_POWER_CHART_TEST
// ======================================================================

using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
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
using System.Windows.Threading;
using FCU_氢燃料电池控.Protocol;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using HC = HandyControl.Controls;

namespace FCU_氢燃料电池控
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        // MVVM ViewModel
        private MainViewModel? _viewModel;

        // ❌ 已删除：private SerialPort? _serialPort; - 现在使用 SerialPortManager
        private DispatcherTimer? _timer;
        private DispatcherTimer? _pollingTimer;  // 电堆信息轮询定时器
        private DispatcherTimer? _bmsBasicPollingTimer;  // BMS基本信息轮询定时器
        private DispatcherTimer? _bmsPackPollingTimer;   // BMS电池组信息轮询定时器
        private DispatcherTimer? _bmsFaultPollingTimer;  // BMS故障信息轮询定时器
        private DispatcherTimer? _chartUpdateTimer;  // 图表数据采集定时器
        private bool _isSerialOpen = false;
        private ProtocolHandler? _protocolHandler;
        private readonly LicenseService? _licenseService;

        // 图表相关字段
        private readonly ObservableCollection<double> _inputPowerValues = new ObservableCollection<double>();
        private readonly ObservableCollection<double> _outputPowerValues = new ObservableCollection<double>();
        private readonly ObservableCollection<DateTime> _timeValues = new ObservableCollection<DateTime>();
        private const int MaxDataPoints = 600;  // 最大数据点数（60秒窗口 × 10点/秒）
        private double _currentInputVoltage = 0;
        private double _currentInputCurrent = 0;
        private double _currentOutputVoltage = 0;
        private double _currentOutputCurrent = 0;

        // 拉载配方相关字段
        private RecipeService? _recipeService;
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
        /// 构造函数（接收LicenseService参数）
        /// </summary>
        public MainWindow(LicenseService licenseService)
        {
            InitializeComponent();
            _licenseService = licenseService;
            
            // ==================== MVVM初始化 ====================
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // 连接ViewModel的SerialPortManager到协议处理
            var serialPortManager = _viewModel.GetSerialPortManager();
            serialPortManager.DataReceived += SerialPort_DataReceived;
            serialPortManager.ConnectionStateChanged += OnSerialConnectionChanged;
            // ==================== MVVM初始化结束 ====================
            
            // 协议处理初始化（保留）
            InitializeProtocolHandler();
            
            // 轮询定时器初始化（保留）
            InitializePollingTimer();
            InitializeBMSPollingTimers();
            
            // 图表初始化（保留）
            InitializePowerChart();
            InitializeChartUpdateTimer();
            
            // 许可证初始化（保留）
            Loaded += MainWindow_Loaded;

            // 拉载配方服务初始化
            _recipeService = new RecipeService();
            LoadRecipeList();
        }
        
        /// <summary>
        /// 串口连接状态改变事件处理
        /// </summary>
        private void OnSerialConnectionChanged(object? sender, bool isConnected)
        {
            _isSerialOpen = isConnected;
            
            if (isConnected)
            {
                // 串口打开时，启动图表采集
                _chartUpdateTimer?.Start();
            }
            else
            {
                // 串口关闭时，停止所有定时器
                _chartUpdateTimer?.Stop();
                
                // 停止电堆信息轮询
                if (_pollingTimer != null && _pollingTimer.IsEnabled)
                {
                    _pollingTimer.Stop();
                }
                
                // 停止BMS轮询定时器
                _bmsBasicPollingTimer?.Stop();
                _bmsPackPollingTimer?.Stop();
                _bmsFaultPollingTimer?.Stop();
            }
        }

        /// <summary>
        /// 窗口加载事件
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLicenseStatusDisplay();
        }

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
        /// 初始化BMS轮询定时器
        /// </summary>
        private void InitializeBMSPollingTimers()
        {
            // BMS基本信息轮询定时器
            _bmsBasicPollingTimer = new DispatcherTimer();
            _bmsBasicPollingTimer.Interval = TimeSpan.FromSeconds(1);
            _bmsBasicPollingTimer.Tick += BMSBasicPollingTimer_Tick;

            // BMS电池组信息轮询定时器
            _bmsPackPollingTimer = new DispatcherTimer();
            _bmsPackPollingTimer.Interval = TimeSpan.FromSeconds(2);
            _bmsPackPollingTimer.Tick += BMSPackPollingTimer_Tick;

            // BMS故障信息轮询定时器
            _bmsFaultPollingTimer = new DispatcherTimer();
            _bmsFaultPollingTimer.Interval = TimeSpan.FromSeconds(2);
            _bmsFaultPollingTimer.Tick += BMSFaultPollingTimer_Tick;
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
        private void SendReadConfigCommand(byte deviceAddress, byte paramTypeHigh, byte paramTypeLow)
        {
            byte[] data = new byte[] { paramTypeHigh, paramTypeLow };
            byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_READ_CONFIG, data);
            SendFrame(frame);
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
            try
            {
                // 检查串口是否打开（静默检查，不显示消息）
                if (!_isSerialOpen)
                    return;

                // 发送读取电堆信息命令
                if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte deviceAddress, showError: false))
                    return;

                byte[] frame = BuildReadFrame(deviceAddress, ProtocolConstants.FUNC_READ_STACK_INFO);
                SendFrame(frame);
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

                UpdateStatus("已停止电堆信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"停止自动刷新失败: {ex.Message}");
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
        private void btn_ReadValveParams_Click(object sender, RoutedEventArgs e)
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
                UpdateStatus("已发送读取阀门参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取阀门参数失败: {ex.Message}");
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

                UpdateStatus("已发送读取拉载参数命令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取拉载参数失败: {ex.Message}");
            }
        }

        #endregion

        #region 参数设置

        // ❌ 已删除：7个压力参数设置按钮事件处理方法
        // btn_SetHighPressureMin_Click, btn_SetHighPressureRed_Click, 
        // btn_SetHighPressureYellow_Click, btn_SetLowPressureMin_Click,
        // btn_SetLowPressureMax_Click, btn_SetHighPressureRange_Click,
        // btn_SetLowPressureRange_Click
        // 已全部被对应的 Command 替代

        /// <summary>
        /// 设置开机第一次时长
        /// </summary>
        private void btn_SetValveFirstOpen_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("开机第一次时长");
        }

        /// <summary>
        /// 设置开机第二次时长
        /// </summary>
        private void btn_SetValveSecondOpen_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("开机第二次时长");
        }

        /// <summary>
        /// 设置正常周期间隔
        /// </summary>
        private void btn_SetValveNormalInterval_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("正常周期间隔");
        }

        /// <summary>
        /// 设置正常打开时长
        /// </summary>
        private void btn_SetValveNormalOpen_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("正常打开时长");
        }

        /// <summary>
        /// 设置关机打开时长
        /// </summary>
        private void btn_SetValveShutdownOpen_Click(object sender, RoutedEventArgs e)
        {
            SetValveParamHelper("关机打开时长");
        }

        /// <summary>
        /// 阀门参数设置辅助方法
        /// </summary>
        private void SetValveParamHelper(string paramName)
        {
            SetParameter(paramName, ProtocolConstants.FUNC_SET_VALVE_PARAMS, BuildValveParamsData);
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

                byte lowDuty = byte.Parse(tbx_FanLowDuty.Text);
                ushort tempMin = ushort.Parse(tbx_TempMin.Text);
                ushort tempMax = ushort.Parse(tbx_TempMax.Text);
                ushort fullSpeedTime = ushort.Parse(tbx_FanFullSpeedTime.Text);
                ushort lowTempCheckTime = ushort.Parse(tbx_LowTempCheckTime.Text);

                byte[] data = new byte[11];
                data[0] = 0x04; data[1] = 0x01;
                data[2] = lowDuty;
                data[3] = (byte)(tempMin >> 8); data[4] = (byte)(tempMin & 0xFF);
                data[5] = (byte)(tempMax >> 8); data[6] = (byte)(tempMax & 0xFF);
                data[7] = (byte)(fullSpeedTime >> 8); data[8] = (byte)(fullSpeedTime & 0xFF);
                data[9] = (byte)(lowTempCheckTime >> 8); data[10] = (byte)(lowTempCheckTime & 0xFF);

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

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 15个时序，每个时序4字节（电流2字节 + 时间2字节）
                byte[] data = new byte[2 + 1 + 15 * 4];
                data[0] = 0x05; // 参数类型ID 高字节
                data[1] = 0x01; // 参数类型ID 低字节
                data[2] = 15;   // 时序总数

                ushort[] currents = {
                    ushort.Parse(tbx_Seq1_Current.Text), ushort.Parse(tbx_Seq2_Current.Text),
                    ushort.Parse(tbx_Seq3_Current.Text), ushort.Parse(tbx_Seq4_Current.Text),
                    ushort.Parse(tbx_Seq5_Current.Text), ushort.Parse(tbx_Seq6_Current.Text),
                    ushort.Parse(tbx_Seq7_Current.Text), ushort.Parse(tbx_Seq8_Current.Text),
                    ushort.Parse(tbx_Seq9_Current.Text), ushort.Parse(tbx_Seq10_Current.Text),
                    ushort.Parse(tbx_Seq11_Current.Text), ushort.Parse(tbx_Seq12_Current.Text),
                    ushort.Parse(tbx_Seq13_Current.Text), ushort.Parse(tbx_Seq14_Current.Text),
                    ushort.Parse(tbx_Seq15_Current.Text)
                };

                ushort[] times = {
                    ushort.Parse(tbx_Seq1_Time.Text), ushort.Parse(tbx_Seq2_Time.Text),
                    ushort.Parse(tbx_Seq3_Time.Text), ushort.Parse(tbx_Seq4_Time.Text),
                    ushort.Parse(tbx_Seq5_Time.Text), ushort.Parse(tbx_Seq6_Time.Text),
                    ushort.Parse(tbx_Seq7_Time.Text), ushort.Parse(tbx_Seq8_Time.Text),
                    ushort.Parse(tbx_Seq9_Time.Text), ushort.Parse(tbx_Seq10_Time.Text),
                    ushort.Parse(tbx_Seq11_Time.Text), ushort.Parse(tbx_Seq12_Time.Text),
                    ushort.Parse(tbx_Seq13_Time.Text), ushort.Parse(tbx_Seq14_Time.Text),
                    ushort.Parse(tbx_Seq15_Time.Text)
                };

                for (int i = 0; i < 15; i++)
                {
                    data[3 + i * 4] = (byte)(currents[i] >> 8);
                    data[4 + i * 4] = (byte)(currents[i] & 0xFF);
                    data[5 + i * 4] = (byte)(times[i] >> 8);
                    data[6 + i * 4] = (byte)(times[i] & 0xFF);
                }

                byte[] frame = BuildFrame(deviceAddress, ProtocolConstants.FUNC_SET_LOAD_PARAMS, data);
                SendFrame(frame);
                UpdateStatus("已发送设置拉载时序参数命令");
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

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                ushort peakCurrent = ushort.Parse(tbx_LoadPeakCurrent.Text);
                ushort lockFactor = ushort.Parse(tbx_LoadLockFactor.Text);
                byte lockFlag = (byte)(chk_LoadLockFlag.IsChecked == true ? 1 : 0);
                ushort minVoltage = ushort.Parse(tbx_LoadMinVoltage.Text);
                ushort currentStep = ushort.Parse(tbx_LoadCurrentStep.Text);

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

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                byte firstCount = byte.Parse(tbx_LoadFirstCount.Text);
                ushort firstInterval = ushort.Parse(tbx_LoadFirstInterval.Text);
                ushort normalInterval = ushort.Parse(tbx_LoadNormalInterval.Text);

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
                ushort scanPeriod = ushort.Parse(tbx_ScanPeriod.Text);

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

        #region 拉载手动控制

        /// <summary>
        /// 发送拉载手动控制指令
        /// </summary>
        /// <param name="commandCode">控制指令码</param>
        /// <param name="commandParams">指令参数（可为null）</param>
        private void SendLoadControlCommand(byte commandCode, byte[]? commandParams = null)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
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
                SendFrame(frame);
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"拉载控制指令发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动拉载
        /// </summary>
        private void btn_LoadStart_Click(object sender, RoutedEventArgs e)
        {
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_START);
            UpdateStatus("已发送启动拉载指令");
        }

        /// <summary>
        /// 暂停拉载
        /// </summary>
        private void btn_LoadPause_Click(object sender, RoutedEventArgs e)
        {
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_PAUSE);
            UpdateStatus("已发送暂停拉载指令");
        }

        /// <summary>
        /// 继续拉载
        /// </summary>
        private void btn_LoadResume_Click(object sender, RoutedEventArgs e)
        {
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_RESUME);
            UpdateStatus("已发送继续拉载指令");
        }

        /// <summary>
        /// 停止拉载
        /// </summary>
        private void btn_LoadStop_Click(object sender, RoutedEventArgs e)
        {
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_STOP);
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
                SendLoadControlCommand(ProtocolConstants.LOAD_CMD_JUMP_SEQUENCE, new byte[] { sequence });
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
                ushort current = ushort.Parse(tbx_TargetCurrent.Text);
                byte[] paramData = new byte[] { (byte)(current >> 8), (byte)(current & 0xFF) };
                SendLoadControlCommand(ProtocolConstants.LOAD_CMD_SET_CURRENT, paramData);
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
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_STEP);
            UpdateStatus("已发送单步执行指令");
        }

        /// <summary>
        /// 重置拉载
        /// </summary>
        private void btn_LoadReset_Click(object sender, RoutedEventArgs e)
        {
            SendLoadControlCommand(ProtocolConstants.LOAD_CMD_RESET);
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
                SendFrame(frame);
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
        private byte[] BuildPressureParamsData()
        {
            ushort highPressureMin = ushort.Parse(_viewModel?.HighPressureMin ?? "100");
            ushort highPressureRed = ushort.Parse(_viewModel?.HighPressureRed ?? "250");
            ushort highPressureYellow = ushort.Parse(_viewModel?.HighPressureYellow ?? "500");
            ushort lowPressureMin = ushort.Parse(_viewModel?.LowPressureMin ?? "35");
            ushort lowPressureMax = ushort.Parse(_viewModel?.LowPressureMax ?? "65");
            ushort highPressureRange = ushort.Parse(_viewModel?.HighPressureRange ?? "5000");
            ushort lowPressureRange = ushort.Parse(_viewModel?.LowPressureRange ?? "250");

            byte[] data = new byte[ProtocolConstants.PRESSURE_PARAMS_DATA_LENGTH];
            data[0] = 0x01; data[1] = 0x01;
            data[2] = (byte)(highPressureMin >> 8); data[3] = (byte)(highPressureMin & 0xFF);
            data[4] = (byte)(highPressureRed >> 8); data[5] = (byte)(highPressureRed & 0xFF);
            data[6] = (byte)(highPressureYellow >> 8); data[7] = (byte)(highPressureYellow & 0xFF);
            data[8] = (byte)(lowPressureMin >> 8); data[9] = (byte)(lowPressureMin & 0xFF);
            data[10] = (byte)(lowPressureMax >> 8); data[11] = (byte)(lowPressureMax & 0xFF);
            data[12] = (byte)(highPressureRange >> 8); data[13] = (byte)(highPressureRange & 0xFF);
            data[14] = (byte)(lowPressureRange >> 8); data[15] = (byte)(lowPressureRange & 0xFF);

            return data;
        }

        /// <summary>
        /// 读取所有阀门参数并构建数据帧（12字节）
        /// </summary>
        private byte[] BuildValveParamsData()
        {
            ushort firstOpen = ushort.Parse(tbx_ValveFirstOpen.Text);
            ushort secondOpen = ushort.Parse(tbx_ValveSecondOpen.Text);
            ushort normalInterval = ushort.Parse(tbx_ValveNormalInterval.Text);
            ushort normalOpen = ushort.Parse(tbx_ValveNormalOpen.Text);
            ushort shutdownOpen = ushort.Parse(tbx_ValveShutdownOpen.Text);

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
            ushort minVoltage = ushort.Parse(tbx_DCDCMinVoltage.Text);
            ushort stopVoltage = ushort.Parse(tbx_DCDCStopVoltage.Text);

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
        private void SendFrame(byte[] frame)
        {
            // 使用ViewModel的SerialPortManager发送数据
            var serialPortManager = _viewModel?.GetSerialPortManager();
            serialPortManager?.SendData(frame);
        }

        #endregion

        #region 串口数据接收处理

        /// <summary>
        /// 串口数据接收事件
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort? sp = sender as SerialPort;
                if (sp == null || _protocolHandler == null)
                    return;

                while (sp.BytesToRead > 0)
                {
                    byte b = (byte)sp.ReadByte();
                    _protocolHandler.ProcessReceivedByte(b);
                }
            }
            catch
            {
                // 异常时忽略
            }
        }

        /// <summary>
        /// 帧接收完成处理
        /// </summary>
        private void OnFrameReceived(byte[] frame)
        {
            try
            {
                byte functionCode = ResponseParser.GetFunctionCode(frame);
                byte dataLength = frame.Length >= 5 ? frame[4] : (byte)0;

                switch (functionCode)
                {
                    case 0x81: // 设备序列号响应
                        HandleSerialNumberResponse(frame);
                        break;

                    case 0x82: // 参数设置响应 或 BMS基本信息响应
                        // 根据数据长度区分: FCU参数设置响应为1字节, BMS基本信息响应为32字节
                        if (dataLength == 32)
                        {
                            // BMS基本信息响应
                            HandleBMSBasicInfoResponse(frame);
                        }
                        else
                        {
                            // FCU参数设置响应
                            HandleSetParamResponse(frame);
                        }
                        break;

                    case 0x83: // BMS电池组信息响应
                        HandleBatteryPackInfoResponse(frame);
                        break;

                    case 0x87: // 电堆信息响应 或 BMS故障信息响应
                        // 根据数据长度区分: FCU电堆信息响应为26字节, BMS故障信息响应为4字节
                        if (dataLength == 4)
                        {
                            // BMS故障信息响应
                            HandleBMSFaultInfoResponse(frame);
                        }
                        else
                        {
                            // FCU电堆信息响应
                            HandleStackInfoResponse(frame);
                        }
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

                    case 0x90: // BMS控制响应
                        HandleBMSControlResponse(frame);
                        break;

                    case 0xF0: // 报警信息上报
                        HandleAlarmReport(frame);
                        break;

                    case 0xF1: // 状态变更上报
                        HandleStateChangeReport(frame);
                        break;

                    case 0xF3: // BMS信息定时上报
                        HandleBMSPeriodicReport(frame);
                        break;

                    case 0xF4: // BMS报警事件上报
                        HandleBMSAlarmReport(frame);
                        break;

                    case 0xF2: // 拉载状态上报
                        HandleLoadStatusReport(frame);
                        break;

                    case 0xF5: // BMS状态变更上报
                        HandleBMSStateChangeReport(frame);
                        break;
                }
            }
            catch
            {
                // 异常时忽略
            }
        }

        /// <summary>
        /// 帧校验失败处理
        /// </summary>
        private void OnFrameValidationFailed()
        {
            Dispatcher.Invoke(() =>
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
            Dispatcher.Invoke(() =>
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
            // 更新功率曲线图数据（DCDC电压/电流）
            UpdateStackInfo(data);
            Dispatcher.Invoke(() =>
            {
                if (_viewModel != null)
                {
                    UpdateViewModelStackInfo(data);
                    _viewModel.StatusMessage = "电堆信息读取成功";
                }
            });
        }

        /// <summary>
        /// 处理参数设置响应
        /// </summary>
        private void HandleSetParamResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseSetParamResponse(frame);
            Dispatcher.Invoke(() =>
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
        /// 处理启动响应
        /// </summary>
        private void HandleStartResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            Dispatcher.Invoke(() =>
            {
                string message = result switch
                {
                    0x00 => "电堆启动成功",
                    0x01 => "启动失败：压力不足",
                    0x02 => "启动失败：设备忙",
                    0x03 => "启动失败：其他原因",
                    _ => $"启动响应异常 (错误码: 0x{result:X2})"
                };
                UpdateStatus(message);
            });
        }

        /// <summary>
        /// 处理关闭响应
        /// </summary>
        private void HandleStopResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            Dispatcher.Invoke(() =>
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
            Dispatcher.Invoke(() =>
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
                        0x02 => "地址无效（必须在1-247范围内）",
                        0x09 => "保存到Flash失败",
                        _ => $"未知错误(0x{result:X2})"
                    };
                    MessageBox.Show($"设备地址修改失败：{errorMessage}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus($"设备地址修改失败: {errorMessage}");
                }
            });
        }

        /// <summary>
        /// 处理配置参数响应
        /// </summary>
        private void HandleConfigParamResponse(byte[] frame)
        {
            ushort paramTypeID = ResponseParser.GetParameterTypeID(frame);

            Dispatcher.Invoke(() =>
            {
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

                    case 0x0901: // 通用参数
                        var generalParams = ResponseParser.ParseGeneralParams(frame);
                        UpdateGeneralParams(generalParams);
                        break;
                }

                UpdateStatus("配置参数读取成功");
            });
        }

        /// <summary>
        /// 处理报警信息上报
        /// </summary>
        private void HandleAlarmReport(byte[] frame)
        {
            AlarmData alarm = ResponseParser.ParseAlarmInfo(frame);
            Dispatcher.Invoke(() =>
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
            Dispatcher.Invoke(() =>
            {
                ShowStateChange(stateChange);
            });
        }

        #endregion

        #region UI更新方法

        /// <summary>
        /// 更新ViewModel中的电堆信息（MVVM模式）
        /// </summary>
        private void UpdateViewModelStackInfo(StackInfoData data)
        {
            if (_viewModel == null) return;
            
            _viewModel.StackState = data.WorkState switch
            {
                0 => "关机",
                1 => "启动中",
                2 => "运行",
                3 => "故障",
                4 => "关机中",
                _ => "未知"
            };
            
            _viewModel.HighPressure = data.HighPressure.ToString();
            _viewModel.LowPressure = data.LowPressure.ToString();
            _viewModel.HydrogenValveState = data.HydrogenValveState == 1 ? "打开" : "关闭";
            _viewModel.DrainValveState = data.DrainValveState == 1 ? "打开" : "关闭";
            _viewModel.DCDCInputVoltage = (data.DCDCInputVoltage / 10.0).ToString("F1");
            _viewModel.DCDCInputCurrent = (data.DCDCInputCurrent / 10.0).ToString("F1");
            _viewModel.DCDCOutputVoltage = (data.DCDCOutputVoltage / 10.0).ToString("F1");
            _viewModel.DCDCOutputCurrent = (data.DCDCOutputCurrent / 10.0).ToString("F1");
            _viewModel.FanSpeed = data.FanSpeed.ToString();
            _viewModel.StackTemperature = (data.StackTemperature / 10.0).ToString("F1");
            _viewModel.FCUTemperature = (data.FCUTemperature / 10.0).ToString("F1");
            _viewModel.RunTime = data.RunTime.ToString();
            _viewModel.FaultCode = data.FaultCode == 0 ? "无故障" : $"0x{data.FaultCode:X2}";
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
            _viewModel.LowPressureMin = data.LowPressureMin.ToString();
            _viewModel.LowPressureMax = data.LowPressureMax.ToString();
            _viewModel.HighPressureRange = data.HighPressureRange.ToString();
            _viewModel.LowPressureRange = data.LowPressureRange.ToString();
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
        private void UpdateValveParams(ValveParams data)
        {
            tbx_ValveFirstOpen.Text = data.FirstOpenTime.ToString();
            tbx_ValveSecondOpen.Text = data.SecondOpenTime.ToString();
            tbx_ValveNormalInterval.Text = data.NormalInterval.ToString();
            tbx_ValveNormalOpen.Text = data.NormalOpenTime.ToString();
            tbx_ValveShutdownOpen.Text = data.ShutdownOpenTime.ToString();
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
            tbx_TempMin.Text = data.TempMin.ToString();
            tbx_TempMax.Text = data.TempMax.ToString();
            tbx_FanFullSpeedTime.Text = data.FullSpeedTime.ToString();
            tbx_LowTempCheckTime.Text = data.LowTempCheckTime.ToString();
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
                    currentBoxes[i].Text = data.Currents[i].ToString();
                    timeBoxes[i].Text = data.Times[i].ToString();
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
                tbx_LoadPeakCurrent.Text = data.PeakCurrent.ToString();
                tbx_LoadLockFactor.Text = data.LockFactor.ToString();
                chk_LoadLockFlag.IsChecked = data.LockFlag == 1;
                tbx_LoadMinVoltage.Text = data.MinVoltage.ToString();
                tbx_LoadCurrentStep.Text = data.CurrentStep.ToString();
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
            Dispatcher.Invoke(() =>
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
        /// 显示报警消息
        /// </summary>
        private void ShowAlarmMessage(AlarmData alarm)
        {
            string title = $"{alarm.GetAlarmLevelName()}报警";
            string message = $"报警类型：{alarm.GetAlarmTypeName()}\n报警数值：{alarm.AlarmValue}";

            MessageBoxImage icon = alarm.AlarmLevel switch
            {
                0x01 => MessageBoxImage.Information,
                0x02 => MessageBoxImage.Warning,
                0x03 => MessageBoxImage.Error,
                0x04 => MessageBoxImage.Error,
                _ => MessageBoxImage.Information
            };

            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            UpdateStatus($"收到报警：{alarm.GetAlarmTypeName()}");
        }

        /// <summary>
        /// 显示状态变更
        /// </summary>
        private void ShowStateChange(StateChangeData stateChange)
        {
            string message = $"{stateChange.GetStateTypeName()}: {stateChange.OldState} → {stateChange.NewState}";
            UpdateStatus($"状态变更：{message}");
        }

        /// <summary>
        /// 处理BMS基本信息响应
        /// </summary>
        private void HandleBMSBasicInfoResponse(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            Dispatcher.Invoke(() =>
            {
                ProcessBMSBasicInfoResponse(data);
            });
        }

        /// <summary>
        /// 处理电池组信息响应
        /// </summary>
        private void HandleBatteryPackInfoResponse(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            Dispatcher.Invoke(() =>
            {
                ProcessBatteryPackInfoResponse(data);
            });
        }

        /// <summary>
        /// 处理BMS故障信息响应
        /// </summary>
        private void HandleBMSFaultInfoResponse(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            Dispatcher.Invoke(() =>
            {
                ProcessBMSFaultInfoResponse(data);
            });
        }

        /// <summary>
        /// 处理BMS控制响应
        /// </summary>
        private void HandleBMSControlResponse(byte[] frame)
        {
            byte result = ResponseParser.ParseControlResponse(frame);
            Dispatcher.Invoke(() =>
            {
                if (result == 0x00)
                {
                    UpdateStatus("BMS控制指令执行成功");
                }
                else
                {
                    string errorMessage = result switch
                    {
                        0x01 => "功能码未定义",
                        0x02 => "数据域长度错误",
                        0x03 => "CRC16校验错误",
                        0x04 => "设备地址不匹配",
                        0x05 => "参数值超出范围",
                        0x06 => "参数类型ID无效",
                        0x07 => "继电器编号无效",
                        0x08 => "控制指令无效",
                        0x09 => "BMS处于故障状态",
                        0x0A => "BMS处于休眠状态",
                        0x0B => "充放电禁止状态",
                        0x0C => "继电器操作失败",
                        0x0D => "MOS管操作失败",
                        0x0E => "数据写入Flash失败",
                        _ => $"未知错误(0x{result:X2})"
                    };
                    UpdateStatus($"BMS控制指令失败: {errorMessage}");
                }
            });
        }

        /// <summary>
        /// 处理BMS信息定时上报
        /// </summary>
        private void HandleBMSPeriodicReport(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            Dispatcher.Invoke(() =>
            {
                ProcessBMSBasicInfoResponse(data);
                UpdateStatus("收到BMS定时上报");
            });
        }

        /// <summary>
        /// 处理BMS报警事件上报
        /// </summary>
        private void HandleBMSAlarmReport(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            if (data.Length >= 5)
            {
                ushort alarmType = (ushort)((data[0] << 8) | data[1]);
                byte alarmLevel = data[2];
                ushort alarmValue = (ushort)((data[3] << 8) | data[4]);

                string alarmTypeName = alarmType switch
                {
                    0x0001 => "单体过压",
                    0x0002 => "单体欠压",
                    0x0003 => "总压过压",
                    0x0004 => "总压欠压",
                    0x0005 => "充电过流",
                    0x0006 => "放电过流",
                    0x0007 => "温度过高",
                    0x0008 => "温度过低",
                    0x0009 => "SOC过低",
                    0x000A => "硬件故障",
                    _ => $"未知类型(0x{alarmType:X4})"
                };

                string levelName = alarmLevel switch
                {
                    0x01 => "提示",
                    0x02 => "警告",
                    0x03 => "严重",
                    0x04 => "紧急",
                    _ => "未知"
                };

                Dispatcher.Invoke(() =>
                {
                    string message = $"BMS报警: {alarmTypeName}\n等级: {levelName}\n数值: {alarmValue}";
                    MessageBox.Show(message, "BMS报警", MessageBoxButton.OK, MessageBoxImage.Warning);
                    UpdateStatus($"收到BMS报警: {alarmTypeName}");
                });
            }
        }

        /// <summary>
        /// 处理BMS状态变更上报
        /// </summary>
        private void HandleBMSStateChangeReport(byte[] frame)
        {
            byte[] data = ResponseParser.GetDataField(frame);
            if (data.Length >= 4)
            {
                byte oldWorkState = data[0];
                byte newWorkState = data[1];
                byte oldChargeState = data[2];
                byte newChargeState = data[3];

                string GetWorkStateName(byte state)
                {
                    return state switch
                    {
                        0x00 => "初始化",
                        0x01 => "待机",
                        0x02 => "充电",
                        0x03 => "放电",
                        0x04 => "预充电",
                        0x05 => "故障",
                        _ => $"未知({state:X2})"
                    };
                }

                string GetChargeStateName(byte state)
                {
                    return state switch
                    {
                        0x00 => "静止",
                        0x01 => "充电",
                        0x02 => "放电",
                        0x03 => "预充电",
                        _ => $"未知({state:X2})"
                    };
                }

                Dispatcher.Invoke(() =>
                {
                    string message = $"BMS状态变更:\n" +
                                   $"工作状态: {GetWorkStateName(oldWorkState)} → {GetWorkStateName(newWorkState)}\n" +
                                   $"充放电状态: {GetChargeStateName(oldChargeState)} → {GetChargeStateName(newChargeState)}";
                    UpdateStatus(message.Replace("\n", " "));
                });
            }
        }

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

        #endregion

        #region BMS电池管理功能

        /// <summary>
        /// 读取BMS基本信息按钮点击事件
        /// </summary>
        private void btn_ReadBMSBasicInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x02读取BMS基本信息请求帧（BMS默认地址为2）
                byte deviceAddress = 2;
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x02, new byte[0]);

                SendFrame(frame);
                UpdateStatus("已发送读取BMS基本信息指令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取BMS基本信息失败: {ex.Message}");
                UpdateStatus($"读取BMS基本信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取电池组信息按钮点击事件
        /// </summary>
        private void btn_ReadBatteryPackInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x03读取电池组信息请求帧
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x03, new byte[0]);

                SendFrame(frame);
                UpdateStatus("已发送读取电池组信息指令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取电池组信息失败: {ex.Message}");
                UpdateStatus($"读取电池组信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取BMS故障信息按钮点击事件
        /// </summary>
        private void btn_ReadBMSFaultInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x07读取BMS故障信息请求帧
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x07, new byte[0]);

                SendFrame(frame);
                UpdateStatus("已发送读取BMS故障信息指令");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"读取BMS故障信息失败: {ex.Message}");
                UpdateStatus($"读取BMS故障信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BMS允许充电按钮点击事件
        /// </summary>
        private void btn_BMS_AllowCharge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x11充放电控制请求帧，控制指令0x01表示允许充电
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] dataField = new byte[] { 0x01 };  // 允许充电
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x11, dataField);

                SendFrame(frame);
                UpdateStatus("已发送允许充电指令");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"允许充电指令发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"允许充电指令发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BMS允许放电按钮点击事件
        /// </summary>
        private void btn_BMS_AllowDischarge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x11充放电控制请求帧，控制指令0x02表示允许放电
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] dataField = new byte[] { 0x02 };  // 允许放电
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x11, dataField);

                SendFrame(frame);
                UpdateStatus("已发送允许放电指令");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"允许放电指令发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"允许放电指令发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BMS允许充放电按钮点击事件
        /// </summary>
        private void btn_BMS_AllowBoth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x11充放电控制请求帧，控制指令0x03表示允许充放电
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] dataField = new byte[] { 0x03 };  // 允许充放电
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x11, dataField);

                SendFrame(frame);
                UpdateStatus("已发送允许充放电指令");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"允许充放电指令发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"允许充放电指令发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BMS禁止充放电按钮点击事件
        /// </summary>
        private void btn_BMS_Forbid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                // 构建功能码0x11充放电控制请求帧，控制指令0x00表示禁止充放电
                byte deviceAddress = 2;  // BMS默认地址为2
                byte[] dataField = new byte[] { 0x00 };  // 禁止充放电
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x11, dataField);

                SendFrame(frame);
                UpdateStatus("已发送禁止充放电指令");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"禁止充放电指令发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"禁止充放电指令发送失败: {ex.Message}");
            }
        }

        #region BMS轮询功能

        /// <summary>
        /// BMS基本信息轮询定时器Tick事件
        /// </summary>
        private void BMSBasicPollingTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                    return;

                byte deviceAddress = 2;  // BMS固定地址为2
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x02, new byte[0]);
                SendFrame(frame);
            }
            catch { }
        }

        /// <summary>
        /// BMS电池组信息轮询定时器Tick事件
        /// </summary>
        private void BMSPackPollingTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                    return;

                byte deviceAddress = 2;  // BMS固定地址为2
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x03, new byte[0]);
                SendFrame(frame);
            }
            catch { }
        }

        /// <summary>
        /// BMS故障信息轮询定时器Tick事件
        /// </summary>
        private void BMSFaultPollingTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                    return;

                byte deviceAddress = 2;  // BMS固定地址为2
                byte[] frame = ProtocolHandler.BuildRequestFrame(deviceAddress, 0x07, new byte[0]);
                SendFrame(frame);
            }
            catch { }
        }

        /// <summary>
        /// 开启BMS基本信息自动刷新
        /// </summary>
        private void btn_StartBMSBasicPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                if (_bmsBasicPollingTimer == null)
                    return;

                // 获取选择的轮询间隔
                ComboBoxItem? selectedItem = cbx_BMSBasicPollingInterval.SelectedItem as ComboBoxItem;
                if (selectedItem != null && selectedItem.Tag != null)
                {
                    int interval = int.Parse(selectedItem.Tag.ToString()!);
                    _bmsBasicPollingTimer.Interval = TimeSpan.FromMilliseconds(interval);
                }

                _bmsBasicPollingTimer.Start();
                btn_StartBMSBasicPolling.IsEnabled = false;
                btn_StopBMSBasicPolling.IsEnabled = true;
                cbx_BMSBasicPollingInterval.IsEnabled = false;
                UpdateStatus("已开启BMS基本信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"开启自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止BMS基本信息自动刷新
        /// </summary>
        private void btn_StopBMSBasicPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bmsBasicPollingTimer == null)
                    return;

                _bmsBasicPollingTimer.Stop();
                btn_StartBMSBasicPolling.IsEnabled = true;
                btn_StopBMSBasicPolling.IsEnabled = false;
                cbx_BMSBasicPollingInterval.IsEnabled = true;
                UpdateStatus("已停止BMS基本信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"停止自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开启BMS电池组信息自动刷新
        /// </summary>
        private void btn_StartBMSPackPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                if (_bmsPackPollingTimer == null)
                    return;

                ComboBoxItem? selectedItem = cbx_BMSPackPollingInterval.SelectedItem as ComboBoxItem;
                if (selectedItem != null && selectedItem.Tag != null)
                {
                    int interval = int.Parse(selectedItem.Tag.ToString()!);
                    _bmsPackPollingTimer.Interval = TimeSpan.FromMilliseconds(interval);
                }

                _bmsPackPollingTimer.Start();
                btn_StartBMSPackPolling.IsEnabled = false;
                btn_StopBMSPackPolling.IsEnabled = true;
                cbx_BMSPackPollingInterval.IsEnabled = false;
                UpdateStatus("已开启BMS电池组信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"开启自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止BMS电池组信息自动刷新
        /// </summary>
        private void btn_StopBMSPackPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bmsPackPollingTimer == null)
                    return;

                _bmsPackPollingTimer.Stop();
                btn_StartBMSPackPolling.IsEnabled = true;
                btn_StopBMSPackPolling.IsEnabled = false;
                cbx_BMSPackPollingInterval.IsEnabled = true;
                UpdateStatus("已停止BMS电池组信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"停止自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开启BMS故障信息自动刷新
        /// </summary>
        private void btn_StartBMSFaultPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isSerialOpen)
                {
                    HC.Growl.Warning("请先打开串口");
                    return;
                }

                if (_bmsFaultPollingTimer == null)
                    return;

                ComboBoxItem? selectedItem = cbx_BMSFaultPollingInterval.SelectedItem as ComboBoxItem;
                if (selectedItem != null && selectedItem.Tag != null)
                {
                    int interval = int.Parse(selectedItem.Tag.ToString()!);
                    _bmsFaultPollingTimer.Interval = TimeSpan.FromMilliseconds(interval);
                }

                _bmsFaultPollingTimer.Start();
                btn_StartBMSFaultPolling.IsEnabled = false;
                btn_StopBMSFaultPolling.IsEnabled = true;
                cbx_BMSFaultPollingInterval.IsEnabled = false;
                UpdateStatus("已开启BMS故障信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"开启自动刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止BMS故障信息自动刷新
        /// </summary>
        private void btn_StopBMSFaultPolling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bmsFaultPollingTimer == null)
                    return;

                _bmsFaultPollingTimer.Stop();
                btn_StartBMSFaultPolling.IsEnabled = true;
                btn_StopBMSFaultPolling.IsEnabled = false;
                cbx_BMSFaultPollingInterval.IsEnabled = true;
                UpdateStatus("已停止BMS故障信息自动刷新");
            }
            catch (Exception ex)
            {
                HC.Growl.Error($"停止自动刷新失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// 处理BMS基本信息响应 (功能码0x82)
        /// </summary>
        private void ProcessBMSBasicInfoResponse(byte[] data)
        {
            try
            {
                if (data.Length < 32)
                {
                    UpdateStatus("BMS基本信息数据长度不足");
                    return;
                }

                // 解析SOC (0.1%)
                ushort soc = (ushort)((data[0] << 8) | data[1]);
                tbk_BMS_SOC.Text = (soc / 10.0).ToString("F1");

                // 解析剩余容量 (0.1Ah)
                ushort remainCapacity = (ushort)((data[2] << 8) | data[3]);
                tbk_BMS_RemainCapacity.Text = (remainCapacity / 10.0).ToString("F1");

                // 解析总电压 (0.1V)
                ushort totalVoltage = (ushort)((data[4] << 8) | data[5]);
                tbk_BMS_TotalVoltage.Text = (totalVoltage / 10.0).ToString("F1");

                // 解析总电流 (0.1A, 有符号)
                short totalCurrent = (short)((data[6] << 8) | data[7]);
                tbk_BMS_TotalCurrent.Text = (totalCurrent / 10.0).ToString("F1");

                // 解析最高单体电压 (0.001V)
                ushort maxCellVoltage = (ushort)((data[8] << 8) | data[9]);
                tbk_BMS_MaxCellVoltage.Text = (maxCellVoltage / 1000.0).ToString("F3");

                // 解析最低单体电压 (0.001V)
                ushort minCellVoltage = (ushort)((data[10] << 8) | data[11]);
                tbk_BMS_MinCellVoltage.Text = (minCellVoltage / 1000.0).ToString("F3");

                // 解析平均单体电压 (0.001V)
                ushort avgCellVoltage = (ushort)((data[12] << 8) | data[13]);
                tbk_BMS_AvgCellVoltage.Text = (avgCellVoltage / 1000.0).ToString("F3");

                // 解析温度信息 (0.1℃, 有符号)
                short maxTemp = (short)((data[15] << 8) | data[16]);
                tbk_BMS_MaxTemp.Text = (maxTemp / 10.0).ToString("F1");

                short minTemp = (short)((data[18] << 8) | data[19]);
                tbk_BMS_MinTemp.Text = (minTemp / 10.0).ToString("F1");

                short avgTemp = (short)((data[20] << 8) | data[21]);
                tbk_BMS_AvgTemp.Text = (avgTemp / 10.0).ToString("F1");

                // 解析BMS工作状态
                byte workState = data[22];
                tbk_BMS_WorkState.Text = workState switch
                {
                    0x00 => "初始化",
                    0x01 => "待机",
                    0x02 => "充电",
                    0x03 => "放电",
                    0x04 => "预充电",
                    0x05 => "故障",
                    _ => $"未知({workState:X2})"
                };

                // 解析充放电状态
                byte chargeDischargeState = data[23];
                tbk_BMS_ChargeDischargeState.Text = chargeDischargeState switch
                {
                    0x00 => "静止",
                    0x01 => "充电",
                    0x02 => "放电",
                    0x03 => "预充电",
                    _ => $"未知({chargeDischargeState:X2})"
                };

                // 解析循环次数
                ushort cycleCount = (ushort)((data[24] << 8) | data[25]);
                tbk_BMS_CycleCount.Text = cycleCount.ToString();

                // 解析SOH (0.1%)
                ushort soh = (ushort)((data[26] << 8) | data[27]);
                tbk_BMS_SOH.Text = (soh / 10.0).ToString("F1");

                // 解析额定容量 (0.1Ah)
                ushort ratedCapacity = (ushort)((data[28] << 8) | data[29]);
                tbk_BMS_RatedCapacity.Text = (ratedCapacity / 10.0).ToString("F1");

                // 解析故障码
                byte faultCode = data[30];
                tbk_BMS_FaultCode.Text = faultCode switch
                {
                    0x00 => "无故障",
                    0x01 => "单体过压",
                    0x02 => "单体欠压",
                    0x03 => "总压过压",
                    0x04 => "总压欠压",
                    0x05 => "充电过流",
                    0x06 => "放电过流",
                    0x07 => "温度过高",
                    0x08 => "温度过低",
                    0x09 => "SOC过低",
                    0x0A => "通信异常",
                    0x0B => "BMS硬件故障",
                    0x0C => "继电器故障",
                    0x0D => "均衡故障",
                    _ => $"未知故障({faultCode:X2})"
                };

                UpdateStatus("BMS基本信息读取成功");
            }
            catch (Exception ex)
            {
                UpdateStatus($"解析BMS基本信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理电池组信息响应 (功能码0x83)
        /// </summary>
        private void ProcessBatteryPackInfoResponse(byte[] data)
        {
            try
            {
                if (data.Length < 16)
                {
                    UpdateStatus("电池组信息数据长度不足");
                    return;
                }

                // 解析串联单体数
                ushort seriesCells = (ushort)((data[0] << 8) | data[1]);
                tbk_BMS_SeriesCells.Text = seriesCells.ToString();

                // 解析并联单体数
                ushort parallelCells = (ushort)((data[2] << 8) | data[3]);
                tbk_BMS_ParallelCells.Text = parallelCells.ToString();

                // 解析额定容量 (0.1Ah)
                ushort ratedCapacity = (ushort)((data[4] << 8) | data[5]);
                tbk_BMS_PackRatedCapacity.Text = (ratedCapacity / 10.0).ToString("F1");

                // 解析额定电压 (0.1V)
                ushort ratedVoltage = (ushort)((data[6] << 8) | data[7]);
                tbk_BMS_PackRatedVoltage.Text = (ratedVoltage / 10.0).ToString("F1");

                // 解析充电截止电压 (0.1V)
                ushort chargeStopVoltage = (ushort)((data[8] << 8) | data[9]);
                tbk_BMS_ChargeStopVoltage.Text = (chargeStopVoltage / 10.0).ToString("F1");

                // 解析放电截止电压 (0.1V)
                ushort dischargeStopVoltage = (ushort)((data[10] << 8) | data[11]);
                tbk_BMS_DischargeStopVoltage.Text = (dischargeStopVoltage / 10.0).ToString("F1");

                // 解析充电截止电流 (0.1A)
                ushort chargeStopCurrent = (ushort)((data[12] << 8) | data[13]);
                tbk_BMS_ChargeStopCurrent.Text = (chargeStopCurrent / 10.0).ToString("F1");

                // 解析放电截止电流 (0.1A)
                ushort dischargeStopCurrent = (ushort)((data[14] << 8) | data[15]);
                tbk_BMS_DischargeStopCurrent.Text = (dischargeStopCurrent / 10.0).ToString("F1");

                UpdateStatus("电池组信息读取成功");
            }
            catch (Exception ex)
            {
                UpdateStatus($"解析电池组信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理BMS故障信息响应 (功能码0x87)
        /// </summary>
        private void ProcessBMSFaultInfoResponse(byte[] data)
        {
            try
            {
                if (data.Length < 4)
                {
                    UpdateStatus("BMS故障信息数据长度不足");
                    return;
                }

                // 解析当前故障码 (16位位掩码)
                ushort currentFault = (ushort)((data[0] << 8) | data[1]);
                tbx_BMS_CurrentFault.Text = ParseBMSFaultCode(currentFault);

                // 解析历史故障码 (16位位掩码)
                ushort historyFault = (ushort)((data[2] << 8) | data[3]);
                tbx_BMS_HistoryFault.Text = ParseBMSFaultCode(historyFault);

                UpdateStatus("BMS故障信息读取成功");
            }
            catch (Exception ex)
            {
                UpdateStatus($"解析BMS故障信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析BMS故障码位掩码
        /// </summary>
        private string ParseBMSFaultCode(ushort faultCode)
        {
            if (faultCode == 0)
            {
                return "无故障";
            }

            var faults = new System.Collections.Generic.List<string>();

            if ((faultCode & 0x0001) != 0) faults.Add("单体过压");
            if ((faultCode & 0x0002) != 0) faults.Add("单体欠压");
            if ((faultCode & 0x0004) != 0) faults.Add("总压过压");
            if ((faultCode & 0x0008) != 0) faults.Add("总压欠压");
            if ((faultCode & 0x0010) != 0) faults.Add("充电过流");
            if ((faultCode & 0x0020) != 0) faults.Add("放电过流");
            if ((faultCode & 0x0040) != 0) faults.Add("温度过高");
            if ((faultCode & 0x0080) != 0) faults.Add("温度过低");
            if ((faultCode & 0x0100) != 0) faults.Add("SOC过低");
            if ((faultCode & 0x0200) != 0) faults.Add("通信异常");
            if ((faultCode & 0x0400) != 0) faults.Add("BMS硬件故障");
            if ((faultCode & 0x0800) != 0) faults.Add("继电器故障");
            if ((faultCode & 0x1000) != 0) faults.Add("均衡故障");

            return string.Join(", ", faults);
        }

        #endregion

        #region 窗口事件

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

                byte deviceAddress = byte.Parse(GetDeviceAddress());

                // 构建63字节数据域：参数类型ID(2) + 时序数(1) + 15组(电流2+时间2)
                byte[] data = new byte[2 + 1 + 15 * 4];
                data[0] = 0x05; // 参数类型ID 高字节
                data[1] = 0x01; // 参数类型ID 低字节
                data[2] = 15;   // 时序总数

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
                    ushort current = ushort.Parse(currentBoxes[i].Text);
                    ushort time = ushort.Parse(timeBoxes[i].Text);
                    data[3 + i * 4] = (byte)(current >> 8);
                    data[4 + i * 4] = (byte)(current & 0xFF);
                    data[5 + i * 4] = (byte)(time >> 8);
                    data[6 + i * 4] = (byte)(time & 0xFF);
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
            }
            catch
            {
                // 静默处理
            }
        }

        #endregion

    }
}
