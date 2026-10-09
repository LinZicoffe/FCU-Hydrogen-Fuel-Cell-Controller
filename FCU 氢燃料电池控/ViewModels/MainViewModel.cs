using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.Managers;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// 创建日期: 2026-02-03
    /// 用途: MVVM架构的ViewModel层，管理UI状态和业务逻辑
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly SerialPortManager _serialPortManager;
        private readonly ParameterManager _parameterManager;
        private const int MaxCommunicationLogEntries = 5000;
        private bool _isDisposed;

        #region 串口相关属性

        private bool _isConnected;
        private string _selectedPortName = string.Empty;
        private int _selectedBaudRate = 9600;
        private string _deviceAddress = "1";
        private string _statusMessage = "就绪";

        /// <summary>
        /// 是否已连接串口
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    // 刷新命令可执行状态
                    ((RelayCommand)OpenSerialCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CloseSerialCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ReadSerialNumberCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ReadStackInfoCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StartStackCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StopStackCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetHighPressureMinCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetHighPressureRedCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetHighPressureYellowCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetLowPressureMinCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetLowPressureMaxCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetHighPressureRangeCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SetLowPressureRangeCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)WritePressureParamsCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 可用串口列表
        /// </summary>
        public ObservableCollection<string> AvailablePorts { get; } = new ObservableCollection<string>();

        /// <summary>
        /// 选中的串口名称
        /// </summary>
        public string SelectedPortName
        {
            get => _selectedPortName;
            set => SetProperty(ref _selectedPortName, value);
        }

        /// <summary>
        /// 可用波特率列表
        /// </summary>
        public ObservableCollection<int> AvailableBaudRates { get; } = new ObservableCollection<int>
        {
            1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200
        };

        /// <summary>
        /// 选中的波特率
        /// </summary>
        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set => SetProperty(ref _selectedBaudRate, value);
        }

        /// <summary>
        /// 设备地址
        /// </summary>
        public string DeviceAddress
        {
            get => _deviceAddress;
            set
            {
                if (SetProperty(ref _deviceAddress, value))
                {
                    // 更新ParameterManager的设备地址
                    if (byte.TryParse(value, out byte address))
                    {
                        _parameterManager.DeviceAddress = address;
                    }
                    // 目标设备可能已切换，压力参数须重新读取后才能免确认整帧写入
                    DeviceAddressChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 状态栏消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        #endregion

        #region 通信日志属性

        private bool _isCommunicationLogPaused;

        public ObservableCollection<CommunicationLogEntry> CommunicationLogs { get; } =
            new ObservableCollection<CommunicationLogEntry>();

        public bool IsCommunicationLogPaused
        {
            get => _isCommunicationLogPaused;
            set => SetProperty(ref _isCommunicationLogPaused, value);
        }

        #endregion

        #region 设备信息属性

        private string _serialNumber = "--";

        /// <summary>
        /// 设备序列号
        /// </summary>
        public string SerialNumber
        {
            get => _serialNumber;
            set => SetProperty(ref _serialNumber, value);
        }

        #endregion

        #region 电堆信息属性

        private string _stackState = "--";
        private string _highPressure = "--";
        private string _lowPressure = "--";
        private string _hydrogenValveState = "--";
        private string _drainValveState = "--";
        private string _dcdcInputVoltage = "--";
        private string _dcdcInputCurrent = "--";
        private string _dcdcOutputVoltage = "--";
        private string _dcdcOutputCurrent = "--";
        private string _fanSpeed = "--";
        private string _fan2EstimatedSpeed = "--";
        private string _stackTemperature = "--";
        private string _fcuTemperature = "--";
        private string _runTime = "--";
        private string _faultCode = "--";

        public string StackState
        {
            get => _stackState;
            set => SetProperty(ref _stackState, value);
        }

        public string HighPressure
        {
            get => _highPressure;
            set => SetProperty(ref _highPressure, value);
        }

        public string LowPressure
        {
            get => _lowPressure;
            set => SetProperty(ref _lowPressure, value);
        }

        public string HydrogenValveState
        {
            get => _hydrogenValveState;
            set => SetProperty(ref _hydrogenValveState, value);
        }

        public string DrainValveState
        {
            get => _drainValveState;
            set => SetProperty(ref _drainValveState, value);
        }

        public string DCDCInputVoltage
        {
            get => _dcdcInputVoltage;
            set => SetProperty(ref _dcdcInputVoltage, value);
        }

        public string DCDCInputCurrent
        {
            get => _dcdcInputCurrent;
            set => SetProperty(ref _dcdcInputCurrent, value);
        }

        public string DCDCOutputVoltage
        {
            get => _dcdcOutputVoltage;
            set => SetProperty(ref _dcdcOutputVoltage, value);
        }

        public string DCDCOutputCurrent
        {
            get => _dcdcOutputCurrent;
            set => SetProperty(ref _dcdcOutputCurrent, value);
        }

        public string FanSpeed
        {
            get => _fanSpeed;
            set => SetProperty(ref _fanSpeed, value);
        }

        /// <summary>
        /// 风扇2无转速反馈，此值仅为上位机按估算占空比换算的显示值。
        /// </summary>
        public string Fan2EstimatedSpeed
        {
            get => _fan2EstimatedSpeed;
            set => SetProperty(ref _fan2EstimatedSpeed, value);
        }

        public string StackTemperature
        {
            get => _stackTemperature;
            set => SetProperty(ref _stackTemperature, value);
        }

        public string FCUTemperature
        {
            get => _fcuTemperature;
            set => SetProperty(ref _fcuTemperature, value);
        }

        public string RunTime
        {
            get => _runTime;
            set => SetProperty(ref _runTime, value);
        }

        public string FaultCode
        {
            get => _faultCode;
            set => SetProperty(ref _faultCode, value);
        }

        #endregion

        #region 压力参数属性

        // 默认值与下位机 fcu_protocol.c 的 g_pressure_params 保持一致
        // (low-pressure-relief-protection@a5a8d63 值: high_min=100 / low_min=20 / low_max=100,
        //  旧出厂组合 300/30/80 与旧默认 35/65 由固件 Flash v8 迁移到 20/100)。
        private string _highPressureMin = "100";      // 固件高压启动门槛 100 KPA
        private string _highPressureRed = "250";
        private string _highPressureYellow = "500";
        private string _highPressureMax = "5000";
        private string _lowPressureMin = "20";        // 固件低压下限 20 KPA（低于此值连续3个扫描周期→CRITICAL停机）
        private string _lowPressureMax = "100";       // 固件低压上限 100 KPA（连续3个100ms采样超限→CRITICAL停机；运行中>80KPA自动泄压）
        private string _highPressureRange = "5000";
        private string _lowPressureRange = "250";

        public string HighPressureMin { get => _highPressureMin; set => SetProperty(ref _highPressureMin, value); }
        public string HighPressureRed { get => _highPressureRed; set => SetProperty(ref _highPressureRed, value); }
        public string HighPressureYellow { get => _highPressureYellow; set => SetProperty(ref _highPressureYellow, value); }
        public string HighPressureMax { get => _highPressureMax; set => SetProperty(ref _highPressureMax, value); }
        public string LowPressureMin { get => _lowPressureMin; set => SetProperty(ref _lowPressureMin, value); }
        public string LowPressureMax { get => _lowPressureMax; set => SetProperty(ref _lowPressureMax, value); }
        public string HighPressureRange { get => _highPressureRange; set => SetProperty(ref _highPressureRange, value); }
        public string LowPressureRange { get => _lowPressureRange; set => SetProperty(ref _lowPressureRange, value); }

        #endregion

        #region 命令

        public ICommand RefreshPortsCommand { get; }
        public ICommand OpenSerialCommand { get; }
        public ICommand CloseSerialCommand { get; }
        public ICommand ReadSerialNumberCommand { get; }
        public ICommand ReadStackInfoCommand { get; }
        public ICommand ReadConfigCommand { get; }
        public ICommand StartStackCommand { get; }
        public ICommand StopStackCommand { get; }
        public ICommand SetHighPressureMinCommand { get; }
        public ICommand SetHighPressureRedCommand { get; }
        public ICommand SetHighPressureYellowCommand { get; }
        public ICommand SetLowPressureMinCommand { get; }
        public ICommand SetLowPressureMaxCommand { get; }
        public ICommand SetHighPressureRangeCommand { get; }
        public ICommand SetLowPressureRangeCommand { get; }
        public ICommand WritePressureParamsCommand { get; }
        public ICommand ClearCommunicationLogCommand { get; }

        /// <summary>
        /// 关闭串口前触发 (订阅者可执行收尾动作, 如归还 BMS 总线仲裁 [SOC计划 §1.3])
        /// </summary>
        public event Action? BeforeSerialClose;

        #endregion

        #region 公开方法

        /// <summary>
        /// 获取串口管理器（供外部连接协议处理事件）
        /// </summary>
        /// <returns>串口管理器实例</returns>
        public SerialPortManager GetSerialPortManager() => _serialPortManager;

        /// <summary>参数管理器 (MainWindow 用于注入固件升级发送拦截钩子)</summary>
        public ParameterManager ParameterManager => _parameterManager;

        public void AppendCommunicationFrame(CommunicationDirection direction, byte[] frame)
        {
            if (IsCommunicationLogPaused || frame == null || frame.Length == 0)
            {
                return;
            }

            CommunicationLogs.Add(new CommunicationLogEntry(DateTime.Now, direction, frame));

            while (CommunicationLogs.Count > MaxCommunicationLogEntries)
            {
                CommunicationLogs.RemoveAt(0);
            }
        }

        #endregion

        #region 构造函数

        public MainViewModel()
        {
            // 初始化管理器
            _serialPortManager = new SerialPortManager();
            _parameterManager = new ParameterManager(_serialPortManager);

            // 订阅事件
            _serialPortManager.ConnectionStateChanged += OnConnectionStateChanged;
            _serialPortManager.ErrorOccurred += OnSerialPortError;
            _parameterManager.StatusChanged += OnParameterStatus;
            _parameterManager.ErrorOccurred += OnParameterError;

            // 初始化命令
            RefreshPortsCommand = new RelayCommand(ExecuteRefreshPorts);
            OpenSerialCommand = new RelayCommand(ExecuteOpenSerial, CanExecuteOpenSerial);
            CloseSerialCommand = new RelayCommand(ExecuteCloseSerial, CanExecuteCloseSerial);
            ReadSerialNumberCommand = new RelayCommand(ExecuteReadSerialNumber, CanExecuteSerialOperation);
            ReadStackInfoCommand = new RelayCommand(ExecuteReadStackInfo, CanExecuteSerialOperation);
            ReadConfigCommand = new RelayCommand(ExecuteReadConfig, CanExecuteSerialOperation);
            StartStackCommand = new RelayCommand(ExecuteStartStack, CanExecuteSerialOperation);
            StopStackCommand = new RelayCommand(ExecuteStopStack, CanExecuteSerialOperation);
            SetHighPressureMinCommand = new RelayCommand(ExecuteSetHighPressureMin, CanExecuteSerialOperation);
            SetHighPressureRedCommand = new RelayCommand(ExecuteSetHighPressureRed, CanExecuteSerialOperation);
            SetHighPressureYellowCommand = new RelayCommand(ExecuteSetHighPressureYellow, CanExecuteSerialOperation);
            SetLowPressureMinCommand = new RelayCommand(ExecuteSetLowPressureMin, CanExecuteSerialOperation);
            SetLowPressureMaxCommand = new RelayCommand(ExecuteSetLowPressureMax, CanExecuteSerialOperation);
            SetHighPressureRangeCommand = new RelayCommand(ExecuteSetHighPressureRange, CanExecuteSerialOperation);
            SetLowPressureRangeCommand = new RelayCommand(ExecuteSetLowPressureRange, CanExecuteSerialOperation);
            WritePressureParamsCommand = new RelayCommand(ExecuteWritePressureParams, CanExecuteSerialOperation);
            ClearCommunicationLogCommand = new RelayCommand(() => CommunicationLogs.Clear());

            // 初始化串口列表
            RefreshPortList();
        }

        #endregion

        #region 命令实现

        private void ExecuteRefreshPorts()
        {
            RefreshPortList();
            StatusMessage = $"已刷新串口列表，检测到 {AvailablePorts.Count} 个可用串口";
        }

        private bool CanExecuteOpenSerial()
        {
            return !IsConnected && !string.IsNullOrEmpty(SelectedPortName);
        }

        private void ExecuteOpenSerial()
        {
            bool success = _serialPortManager.Open(SelectedPortName, SelectedBaudRate);
            if (success)
            {
                StatusMessage = $"串口 {SelectedPortName} 已打开";
            }
        }

        private bool CanExecuteCloseSerial()
        {
            return IsConnected;
        }

        private void ExecuteCloseSerial()
        {
            BeforeSerialClose?.Invoke();
            _serialPortManager.Close();
            StatusMessage = "串口已关闭";
        }

        private bool CanExecuteSerialOperation()
        {
            return IsConnected;
        }

        private void ExecuteReadSerialNumber()
        {
            _parameterManager.ReadSerialNumber();
        }

        private void ExecuteReadStackInfo()
        {
            _parameterManager.ReadStackInfo();
        }

        private void ExecuteStartStack()
        {
            _parameterManager.StartStack();
        }

        private void ExecuteStopStack()
        {
            _parameterManager.StopStack();
        }

        private void ExecuteReadConfig()
        {
            // 读取所有配置参数
            StatusMessage = "读取配置参数功能需要完整实现";
        }

        private void ExecuteSetHighPressureMin()
        {
            SetPressureParameter("高压最小值");
        }

        private void ExecuteSetHighPressureRed()
        {
            SetPressureParameter("高压红警值");
        }

        private void ExecuteSetHighPressureYellow()
        {
            SetPressureParameter("高压黄警值");
        }

        private void ExecuteSetLowPressureMin()
        {
            SetPressureParameter("低压下限值");
        }

        private void ExecuteSetLowPressureMax()
        {
            SetPressureParameter("低压上限值");
        }

        private void ExecuteSetHighPressureRange()
        {
            SetPressureParameter("高压量程");
        }

        private void ExecuteSetLowPressureRange()
        {
            SetPressureParameter("低压量程");
        }

        /// <summary>
        /// 整帧写入压力参数（0x02，18B）。红/黄阈值不在界面展示，
        /// 取 ViewModel 当前值（设备回读值或固件默认值）原样回写，避免整帧覆盖时篡改未展示字段。
        /// </summary>
        private void ExecuteWritePressureParams()
        {
            SetPressureParameter("压力参数");
        }

        /// <summary>
        /// 参数本地校验失败（构建帧时抛出异常）。VM 不引用 UI，
        /// 由 MainWindow 订阅后弹 Growl，与 code-behind 参数组的反馈方式一致。
        /// </summary>
        public event EventHandler<string>? ParameterValidationFailed;

        /// <summary>设备地址被用户修改。多机共总线时切换目标后，按设备缓存的参数须作废。</summary>
        public event EventHandler? DeviceAddressChanged;

        private void SetPressureParameter(string parameterName)
        {
            try
            {
                // 构建压力参数数据（内部含合法性校验，非法时抛异常）
                byte[] data = BuildPressureParamsData();
                _parameterManager.SetPressureParameter(data, parameterName);
            }
            catch (Exception ex)
            {
                string message = $"设置{parameterName}失败: {ex.Message}";
                StatusMessage = message;
                ParameterValidationFailed?.Invoke(this, message);
            }
        }

        /// <summary>
        /// 解析压力参数字段并做范围校验。
        /// 固件 fcu_protocol_set_pressure_params() 对整帧做全字段校验（min==0、min/红/黄超量程、
        /// 红黄序、区间序任一不符即拒收整帧）；本地逐条对齐并先给出字段级原因，
        /// 避免用户只看到笼统的 0x82「参数错误」。
        /// </summary>
        private static ushort ParsePressureField(string text, string fieldName, int min)
        {
            if (!ushort.TryParse(text?.Trim(), out ushort value))
            {
                throw new ArgumentException($"{fieldName}「{text}」不是合法整数");
            }
            if (value < min)
            {
                throw new ArgumentException($"{fieldName}不能小于 {min}（当前 {value} KPA）");
            }
            return value;
        }

        private byte[] BuildPressureParamsData()
        {
            // min==0 会被固件整帧拒收；量程为 0 经「min>量程」同样被拒，本地先行拦截提示更明确
            ushort highPressureMin = ParsePressureField(HighPressureMin, "高压最小值", 1);
            ushort highPressureRed = ParsePressureField(HighPressureRed, "高压红警值", 1);
            ushort highPressureYellow = ParsePressureField(HighPressureYellow, "高压黄警值", 1);
            ushort highPressureMax = ParsePressureField(HighPressureMax, "高压上限告警值", 1);
            ushort lowPressureMin = ParsePressureField(LowPressureMin, "低压下限值", 1);
            ushort lowPressureMax = ParsePressureField(LowPressureMax, "低压上限值", 1);
            ushort highPressureRange = ParsePressureField(HighPressureRange, "高压量程", 1);
            ushort lowPressureRange = ParsePressureField(LowPressureRange, "低压量程", 1);

            // 以下校验与固件 fcu_protocol_set_pressure_params() 的拒收条件逐条对齐
            if (highPressureRed >= highPressureYellow)
            {
                throw new ArgumentException(
                    $"高压红警值({highPressureRed})必须小于高压黄警值({highPressureYellow})，固件会拒绝整帧");
            }

            if (highPressureMax <= highPressureYellow)
            {
                throw new ArgumentException(
                    $"高压上限告警值({highPressureMax})必须大于高压黄警值({highPressureYellow})");
            }

            if (highPressureMax <= highPressureMin)
            {
                // 比固件更严（固件允许 min 与 max 交叉，min 是独立启动门限），
                // 拦截是为了避免误设出“上限低于启动门限”的组合
                throw new ArgumentException(
                    $"高压上限告警值({highPressureMax})必须大于高压最小值({highPressureMin})");
            }

            if (highPressureMin > highPressureRange)
            {
                throw new ArgumentException(
                    $"高压最小值({highPressureMin})不能超过高压传感器量程({highPressureRange})，固件会拒绝整帧");
            }

            if (highPressureRed > highPressureRange)
            {
                throw new ArgumentException(
                    $"高压红警值({highPressureRed})不能超过高压传感器量程({highPressureRange})，固件会拒绝整帧");
            }

            if (highPressureYellow > highPressureRange)
            {
                throw new ArgumentException(
                    $"高压黄警值({highPressureYellow})不能超过高压传感器量程({highPressureRange})，固件会拒绝整帧");
            }

            if (highPressureMax > highPressureRange)
            {
                throw new ArgumentException(
                    $"高压上限告警值({highPressureMax})不能超过高压传感器量程({highPressureRange})");
            }

            // 固件同样拒收 low_max<=low_min，本地先行给出字段级原因
            if (lowPressureMax <= lowPressureMin)
            {
                throw new ArgumentException(
                    $"低压上限值({lowPressureMax})必须大于低压下限值({lowPressureMin})");
            }

            if (lowPressureMin > lowPressureRange)
            {
                throw new ArgumentException(
                    $"低压下限值({lowPressureMin})不能超过低压传感器量程({lowPressureRange})，固件会拒绝整帧");
            }

            if (lowPressureMax > lowPressureRange)
            {
                throw new ArgumentException(
                    $"低压上限值({lowPressureMax})不能超过低压传感器量程({lowPressureRange})");
            }

            byte[] data = new byte[ProtocolConstants.PRESSURE_PARAMS_DATA_LENGTH];
            data[0] = 0x01; data[1] = 0x01;
            data[2] = (byte)(highPressureMin >> 8); data[3] = (byte)(highPressureMin & 0xFF);
            data[4] = (byte)(highPressureRed >> 8); data[5] = (byte)(highPressureRed & 0xFF);
            data[6] = (byte)(highPressureYellow >> 8); data[7] = (byte)(highPressureYellow & 0xFF);
            data[8] = (byte)(highPressureMax >> 8); data[9] = (byte)(highPressureMax & 0xFF);
            data[10] = (byte)(lowPressureMin >> 8); data[11] = (byte)(lowPressureMin & 0xFF);
            data[12] = (byte)(lowPressureMax >> 8); data[13] = (byte)(lowPressureMax & 0xFF);
            data[14] = (byte)(highPressureRange >> 8); data[15] = (byte)(highPressureRange & 0xFF);
            data[16] = (byte)(lowPressureRange >> 8); data[17] = (byte)(lowPressureRange & 0xFF);

            return data;
        }

        #endregion

        #region 事件处理

        private void OnConnectionStateChanged(object? sender, bool isConnected)
        {
            IsConnected = isConnected;
        }

        private void OnSerialPortError(object? sender, string errorMessage)
        {
            StatusMessage = $"错误: {errorMessage}";
        }

        private void OnParameterStatus(object? sender, string statusMessage)
        {
            StatusMessage = statusMessage;
        }

        private void OnParameterError(object? sender, string errorMessage)
        {
            StatusMessage = $"错误: {errorMessage}";
        }

        #endregion

        #region 私有方法

        private void RefreshPortList()
        {
            string? currentSelection = SelectedPortName;
            AvailablePorts.Clear();

            var ports = SerialPortManager.GetAvailablePorts();
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
            }

            // 恢复之前的选择
            if (!string.IsNullOrEmpty(currentSelection) && AvailablePorts.Contains(currentSelection))
            {
                SelectedPortName = currentSelection;
            }
            else if (AvailablePorts.Count > 0)
            {
                SelectedPortName = AvailablePorts[0];
            }
        }

        #endregion

        #region IDisposable实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // 取消事件订阅
                    _serialPortManager.ConnectionStateChanged -= OnConnectionStateChanged;
                    _serialPortManager.ErrorOccurred -= OnSerialPortError;
                    _parameterManager.StatusChanged -= OnParameterStatus;
                    _parameterManager.ErrorOccurred -= OnParameterError;

                    // 释放管理器
                    _serialPortManager?.Dispose();
                }
                _isDisposed = true;
            }
        }

        #endregion
    }
}
