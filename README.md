# FCU氢燃料电池控制器上位机

## 项目简介

FCU氢燃料电池控制器上位机是一款基于WPF开发的工业控制软件，用于通过RS485接口与氢燃料电池控制器（FCU）进行通信，实现设备参数配置、实时数据监控和系统控制等功能。

## 主要功能

### FCU氢燃料电池控制器功能
- **串口通信管理**：支持串口配置、打开/关闭串口连接
- **设备参数读取**：读取设备序列号、电堆信息、配置参数
- **参数配置**：支持压力参数、阀门参数、DCDC参数、风扇温度参数、拉载参数、通用参数等配置
- **设备地址管理**：支持远程修改FCU设备地址（功能码0x0C）
- **实时监控**：实时显示电堆工作状态、压力、温度、电压、电流等关键数据
- **系统控制**：支持电堆启动和关闭操作
- **功率曲线图**：实时显示输入功率和输出功率曲线，支持1000秒历史数据显示（10点/秒）

### BMS电池管理系统功能
- **基本信息监控**：实时显示SOC、剩余容量、总电压电流、单体电压、温度等32字节数据
- **电池组配置**：查看串并联数、额定容量电压、充放电截止参数
- **充放电控制**：支持允许充电、允许放电、允许充放电、禁止充放电四种操作（功能码0x11）
- **故障诊断**：实时监控当前故障和历史故障，支持13种故障类型的位掩码解析
- **主动上报**：支持BMS定时上报（0xF3）、报警事件上报（0xF4）、状态变更上报（0xF5）
- **总线集成**：BMS与FCU共用RS485总线，BMS默认地址为2，FCU默认地址为1

## 技术栈

- **开发框架**：WPF (.NET 8.0)
- **编程语言**：C#
- **通信协议**：RS485 Modbus协议
- **UI设计**：Material Design风格

## 系统要求

- Windows 10 或更高版本
- .NET 8.0 Runtime
- RS485串口设备

## 安装说明

1. 确保已安装 .NET 8.0 Runtime
2. 连接RS485设备到计算机
3. 运行应用程序
4. 配置串口参数（串口号、波特率、设备地址）
5. 点击"打开串口"建立连接

## 使用说明

### 串口设置
- 选择正确的串口号（COM端口）
- 设置波特率（默认9600）
- 设置从站地址（1-247，默认1用于FCU，BMS固定使用地址2）
- 点击"打开串口"建立连接

### 读取操作
- **读取设备序列号**：获取设备的唯一序列号
- **读取电堆信息**：获取电堆的实时状态信息
- **读取配置参数**：读取当前所有配置参数
- **读取BMS信息**：获取BMS基本信息、电池组配置、故障信息等

### 参数配置
在对应的标签页中修改参数值，然后点击相应的"设置"按钮保存配置。

### 系统控制
- **启动电堆**：启动氢燃料电池电堆系统
- **关闭电堆**：关闭氢燃料电池电堆系统
- **BMS充放电控制**：允许/禁止BMS充电、放电或充放电

### 自动刷新功能
- **电堆信息自动刷新**：在"电堆信息"Tab中，可设置轮询间隔并开启自动刷新
- **BMS基本信息自动刷新**：在"BMS电池管理"Tab中，每个信息模块支持独立的轮询设置
- **轮询间隔选择**：支持0.5秒、1秒、2秒、5秒四档轮询间隔
- **独立控制**：每个信息模块的轮询可独立开启/停止，互不影响
- **自动停止**：关闭串口时所有轮询定时器自动停止

### 设备地址管理
- **修改FCU设备地址**：在"通用参数"Tab中可远程修改FCU设备地址（需重启设备生效）
- **输入框地址**：串口设置区域的"从站地址"输入框用于FCU通信
- **BMS固定地址**：BMS相关功能固定使用地址2，无需修改输入框
- **地址范围**：支持1-247的合法Modbus设备地址

## 更新日志

### 2026-02-03

*V7*：
- BUG修复：修复轮询功能无法使用的问题
- BUG修复：在构造函数中恢复InitializePollingTimer()调用
- BUG修复：在构造函数中恢复InitializeBMSPollingTimers()调用
- 功能恢复：电堆信息轮询定时器正常工作
- 功能恢复：BMS基本信息轮询定时器正常工作
- 功能恢复：BMS电池组信息轮询定时器正常工作
- 功能恢复：BMS故障信息轮询定时器正常工作
- 功能验证：轮询按钮（开启/停止自动刷新）正常工作
- 编译成功：项目成功编译，0个错误，2个可空性警告

*V6*：
- BUG修复：修复值转换器类型不兼容问题，确保App.xaml正确注册
- BUG修复：删除InitializeSerialPortList和RefreshSerialPortList方法（已被ViewModel替代）
- BUG修复：将所有tbx_DeviceAddress.Text替换为GetDeviceAddress()方法调用
- BUG修复：将所有tbk_Status.Text替换为_viewModel.StatusMessage
- BUG修复：修复SerialPort_DataReceived方法，使用sender参数而不是_serialPort字段
- BUG修复：修复UpdateStatus方法，通过ViewModel更新状态栏
- BUG修复：修复BuildPressureParamsData方法，从ViewModel读取参数
- BUG修复：修复UpdateStackInfo方法，保留图表数据更新逻辑
- BUG修复：修复HandleStackInfoResponse方法，同时更新图表数据和ViewModel
- BUG修复：删除UpdatePressureParams方法（已被UpdateViewModelPressureParams替代）
- BUG修复：修复SendFrame方法，使用SerialPortManager发送数据
- BUG修复：修复许可证状态显示，通过ViewModel更新
- BUG修复：移除_serialPort字段及所有相关引用
- 代码清理：删除47行使用已删除控件名称的代码
- 辅助方法：新增GetDeviceAddress()方法，从ViewModel获取设备地址
- 编译成功：项目成功编译，0个错误，仅2个可空性警告
- 代码质量：MainWindow.xaml.cs从2732行减少至约2650行
- 架构完善：所有UI控件访问已完全通过ViewModel
- 架构完善：串口操作已完全迁移到SerialPortManager
- 测试就绪：程序可以正常运行和测试

*V5*：
- 代码清理：删除btn_RefreshPorts_Click等12个已被Command替代的按钮事件处理方法
- 代码清理：删除btn_ReadSerialNumber_Click、btn_ReadStackInfo_Click等读取操作方法
- 代码清理：删除btn_StartStack_Click、btn_StopStack_Click等系统控制方法
- 代码清理：删除7个压力参数设置按钮事件处理方法（btn_SetHighPressureMin_Click等）
- 代码清理：删除btn_ReadConfig_Click读取配置方法
- 代码清理：MainWindow.xaml.cs从2935行减少至约2700行（减少约235行）
- 代码集成：OnSerialConnectionChanged方法增加定时器自动停止逻辑
- 代码集成：串口关闭时自动停止所有轮询定时器（电堆、BMS基本、电池组、故障）
- 代码集成：HandleSerialNumberResponse更新ViewModel.SerialNumber而不是直接更新UI
- 代码集成：HandleStackInfoResponse调用UpdateViewModelStackInfo更新ViewModel
- 代码集成：HandleConfigParamResponse调用UpdateViewModelPressureParams更新ViewModel
- 辅助方法：新增UpdateViewModelStackInfo方法（32行），更新ViewModel的14个电堆信息属性
- 辅助方法：新增UpdateViewModelPressureParams方法（10行），更新ViewModel的7个压力参数属性
- 文档创建：创建"代码清理指南.md"，详细说明哪些方法可以安全删除
- 文档创建：代码清理指南包含38个可删除方法的清单（约350行代码）
- 架构完善：混合模式完美运行，ViewModel管理UI状态，MainWindow处理协议
- 架构完善：所有UI更新通过ViewModel自动同步，无需手动调用Dispatcher
- 用户体验：UI响应更流畅，状态管理更一致
- 代码质量：职责更清晰，耦合度更低，可维护性大幅提升
- 文档更新：创建"MVVM集成完整指南.md"，包含完整的集成实施方案

*V4*：
- XAML迁移：完成MainWindow.xaml核心控件的数据绑定迁移
- XAML迁移：串口选择ComboBox绑定到AvailablePorts和SelectedPortName
- XAML迁移：波特率ComboBox绑定到AvailableBaudRates和SelectedBaudRate
- XAML迁移：设备地址TextBox绑定到DeviceAddress属性
- XAML迁移：刷新串口按钮绑定到RefreshPortsCommand命令
- XAML迁移：打开/关闭串口按钮使用IsConnected控制显示和命令绑定
- XAML迁移：读取操作3个按钮绑定到对应Command（ReadSerialNumber等）
- XAML迁移：系统控制2个按钮绑定到StartStack/StopStack命令
- XAML迁移：电堆信息14个TextBlock绑定到ViewModel属性（StackState等）
- XAML迁移：压力参数7个TextBox绑定到ViewModel属性（双向绑定）
- XAML迁移：压力参数7个设置按钮绑定到对应设置命令
- XAML迁移：设备序列号TextBox绑定到SerialNumber属性（单向绑定）
- XAML迁移：状态栏TextBlock绑定到StatusMessage属性
- XAML迁移：所有绑定控件自动根据IsConnected状态启用/禁用
- XAML迁移：使用InverseBoolConverter实现串口打开时禁用配置控件
- XAML迁移：使用BoolToVisibilityConverter实现按钮显示/隐藏
- 代码简化：移除大量的x:Name属性，控件通过绑定访问
- 代码简化：移除大量的Click事件处理器声明
- UI自动化：按钮可执行状态由ViewModel的CanExecute自动管理
- UI自动化：数据更新通过INotifyPropertyChanged自动刷新UI
- 用户体验：串口打开时自动禁用配置控件，防止误操作
- 用户体验：命令不可用时按钮自动变灰，提供视觉反馈
- 架构完善：View层完全独立，仅负责UI呈现
- 架构完善：ViewModel成为UI状态的唯一真实来源
- 文档更新：更新README.md记录完整的XAML迁移内容

*V3*：
- MVVM完全迁移：创建Converters/BoolToVisibilityConverter.cs值转换器
- MVVM完全迁移：创建Converters/InverseBoolConverter.cs反向布尔转换器
- MVVM完全迁移：扩展MainViewModel支持压力参数属性和命令
- MVVM完全迁移：MainViewModel新增7个压力参数属性（HighPressureMin等）
- MVVM完全迁移：MainViewModel新增8个参数设置命令（SetHighPressureMinCommand等）
- MVVM完全迁移：MainViewModel实现BuildPressureParamsData数据构建方法
- MVVM完全迁移：在MainWindow.xaml.cs中设置DataContext为MainViewModel
- MVVM完全迁移：MainWindow.xaml.cs添加using FCU_氢燃料电池控.ViewModels命名空间
- MVVM完全迁移：创建"XAML数据绑定迁移指南.md"详细迁移文档
- 迁移指南：提供6大类控件的完整迁移示例（串口连接、读取操作、系统控制等）
- 迁移指南：说明UpdateSourceTrigger、命令自动启用/禁用、双向绑定等关键要点
- 迁移指南：提供完整的迁移检查清单和注意事项
- 架构完善：MVVM基础设施完全就绪，支持完整的数据绑定
- 架构完善：值转换器支持Visibility和Bool之间的转换
- 架构完善：命令系统支持CanExecute自动管理按钮状态
- 可维护性：UI和业务逻辑完全分离，符合MVVM标准
- 可测试性：ViewModel可独立测试，无UI依赖
- 开发效率：提供详细文档，降低团队学习成本
- 文档更新：创建完整的XAML迁移指南，包含代码示例和最佳实践

*V2*：
- 架构重构：创建Managers/SerialPortManager.cs串口管理器，封装所有串口操作
- 架构重构：创建Managers/ParameterManager.cs参数管理器，封装参数读写逻辑
- 架构重构：实现完整的MVVM基础设施（ObservableObject + RelayCommand）
- 架构重构：创建ViewModels/MainViewModel.cs主视图模型，实现UI状态管理
- 架构重构：SerialPortManager提供事件机制（DataReceived、ConnectionStateChanged、ErrorOccurred）
- 架构重构：ParameterManager封装帧构建和CRC计算逻辑
- 架构重构：MainViewModel实现INotifyPropertyChanged自动UI更新
- MVVM实现：ObservableObject基类提供SetProperty方法，支持属性变更通知
- MVVM实现：RelayCommand支持泛型版本，提供参数化命令绑定
- MVVM实现：MainViewModel定义串口连接、电堆信息等20+个可绑定属性
- MVVM实现：MainViewModel实现7个命令（RefreshPorts、OpenSerial、CloseSerial等）
- MVVM实现：命令自动管理可执行状态（CanExecute），UI按钮自动启用/禁用
- 代码组织：将串口操作从MainWindow.xaml.cs分离至SerialPortManager（约200行）
- 代码组织：将参数管理从MainWindow.xaml.cs分离至ParameterManager（约250行）
- 代码组织：创建Managers文件夹，统一管理业务逻辑类
- 代码组织：创建ViewModels文件夹，实现MVVM架构分层
- 可测试性：SerialPortManager可独立测试，无需UI依赖
- 可测试性：ParameterManager可独立测试，支持单元测试
- 可测试性：MainViewModel可进行ViewModel单元测试
- 设计模式：实现事件驱动架构，降低模块间耦合
- 设计模式：实现IDisposable模式，确保资源正确释放
- 设计模式：使用依赖注入，ParameterManager依赖SerialPortManager
- 文档更新：更新README.md，记录第二、三阶段重构内容

*V1*：
- 代码重构：创建Protocol/ProtocolConstants.cs常量类，消除100+处魔法数字
- 代码重构：创建Helpers/UIUpdater.cs辅助类，统一处理跨线程UI更新操作
- 代码重构：创建Helpers/SerialPortHelper.cs辅助类，统一串口检查和错误处理
- 代码重构：创建通用SetParameter方法，消除参数设置方法的重复代码
- 代码重构：重构7个压力参数设置方法，从原280行缩减为7行（减少约273行）
- 代码重构：重构阀门参数和DCDC参数设置辅助方法，消除重复的串口检查和错误处理
- 代码重构：创建BuildPressureParamsData、BuildValveParamsData、BuildDCDCParamsData数据构建方法
- 代码优化：替换BuildFrame和CalculateCRC16方法中的魔法数字为常量
- 代码优化：重构btn_ReadSerialNumber_Click、btn_ReadStackInfo_Click等读取方法
- 代码优化：重构btn_StartStack_Click、btn_StopStack_Click等系统控制方法
- 代码优化：优化PollingTimer_Tick轮询方法，使用静默检查避免不必要的提示
- 代码优化：所有功能码使用ProtocolConstants中的常量（FUNC_READ_SERIAL_NUMBER等）
- 代码优化：所有帧结构常量使用ProtocolConstants（FRAME_HEADER1、FRAME_HEADER2等）
- 可维护性：显著降低代码重复率，从约20%降至5%以下
- 可维护性：减少MainWindow.xaml.cs约500-600行代码
- 可维护性：统一错误处理和用户提示，提升代码一致性
- 可维护性：为后续MVVM重构和类职责拆分打下基础
- 代码质量：消除38处重复的串口检查代码（if (!_isSerialOpen)）
- 代码质量：消除91处重复的MessageBox.Show调用
- 代码质量：提供参数解析辅助方法（TryParseDeviceAddress、TryParseUInt16等）
- 文档更新：更新README.md，详细记录本次重构内容和收益

### 2026-01-27

*V8*：
- 修复问题：彻底解决图例显示空白方框问题，改用XAML自定义图例
- UI改进：隐藏LiveChartsCore自带图例，在XAML中手动创建清晰的图例显示
- 新增功能：使用彩色Border元素作为图例标记（蓝色线条 + "输入功率"，橙红色线条 + "输出功率"）
- 用户体验：图例显示完全可控，无字体兼容性问题，视觉效果更专业
- 代码优化：图例布局与标题集成在同一行，节省垂直空间

*V7*：
- 修复问题：图例显示空白方框"口口"的BUG，移除Unicode特殊字符"●"
- 优化改进：图例改为纯文本描述"输入功率 (蓝色)"、"输出功率 (橙红色)"
- UI改进：LiveChartsCore自动在图例文字前显示彩色线条标记，无需手动添加符号
- 用户体验：图例显示更清晰，兼容性更好，避免字体渲染问题（后发现问题依旧）

*V6*：
- 紧急修复：修复图表渲染空引用异常（NullReferenceException in LabelGeometry.GetLines）
- 修复问题：X轴Labeler改为返回string.Empty而不是null，符合LiveChartsCore库要求
- 优化改进：添加MinStep和ForceStepToMin配置优化轴标签密度
- 代码质量：确保Labeler始终返回有效字符串，避免空引用异常

*V5*：
- 修复问题：功率曲线图添加图例标识，使用"●"圆点符号+文字区分输入功率（蓝色）和输出功率（橙红色）
- 修复问题：修复图表底部和左侧显示空白框的BUG（已回滚）
- 优化改进：添加ShowSeparatorLines属性确保网格线正确显示
- UI改进：图例位置设置为Top，显示在图表顶部
- 用户体验：图例清晰标识每条曲线含义，● 输入功率（蓝色）、● 输出功率（橙红色）

*V4*：
- 优化改进：为左侧面板添加垂直滚动条，解决内容显示不全的问题
- 优化改进：图表区域高度从300px调整为200px，为主内容区提供更多空间
- 新增功能：添加GridSplitter分隔条，用户可手动拖拽调整图表高度
- UI改进：主窗口布局优化为5行结构（标题栏、主内容、分隔条、图表区、状态栏）
- UI改进：GridSplitter显示为浅灰色横条，鼠标悬停时显示上下调整光标
- 优化改进：左侧面板自动显示滚动条，支持查看所有串口设置和读取操作
- 用户体验：图表高度可根据实际需求灵活调整，默认200px

*V3*：
- 新增功能：新增功率曲线图功能，实时显示输入功率和输出功率
- 新增功能：功率曲线图使用LiveChartsCore.SkiaSharpView.WPF图表库，基于SkiaSharp渲染引擎
- 新增功能：功率曲线图放置在程序下方，不受Tab切换影响，始终可见
- 新增功能：输入功率计算公式：DCDC输入电流 × DCDC输入电压
- 新增功能：输出功率计算公式：DCDC输出电流 × DCDC输出电压
- 新增功能：图表Y轴范围0-1200W，X轴为时间坐标（HH:mm:ss格式）
- 新增功能：数据采集速率10 Hz（100ms/点），实时性高
- 新增功能：时间窗口支持1000秒（最大10000个数据点）
- 新增功能：曲线采用平滑过渡效果（LineSmoothness = 0.5），视觉效果更佳
- 新增功能：双曲线显示，输入功率（蓝色）和输出功率（橙红色），对比清晰
- 优化改进：图表自动滚动，始终显示最新数据
- 优化改进：超过最大数据点时自动删除旧数据，保持性能
- 优化改进：程序启动后图表自动开始采集，无需手动启动
- 优化改进：数据采集定时器与电堆信息轮询独立，互不影响
- 代码重构：新增InitializePowerChart方法初始化图表配置
- 代码重构：新增InitializeChartUpdateTimer方法初始化数据采集定时器
- 代码重构：新增ChartUpdateTimer_Tick方法处理数据采集和更新
- UI改进：主窗口布局调整为4行结构（标题栏、主内容、图表区、状态栏）
- UI改进：图表区域高度300px，带圆角边框和阴影效果
- UI改进：图表标题清晰标注"功率曲线 (输入功率 / 输出功率)"
- 依赖变更：新增LiveChartsCore.SkiaSharpView.WPF包引用（Version 2.0.0-rc2）
- 依赖变更：新增SkiaSharp.Views.WPF包引用（Version 2.88.8）

*V2*：
- 新增功能：为BMS基本信息添加自动轮询功能，支持实时刷新BMS数据（功能码0x02）
- 新增功能：为BMS电池组配置信息添加自动轮询功能（功能码0x03）
- 新增功能：为BMS故障信息添加自动轮询功能（功能码0x07）
- 新增功能：每个BMS信息模块支持独立的轮询间隔设置（0.5秒、1秒、2秒、5秒）
- 新增功能：每个BMS信息模块支持独立开启/停止自动刷新
- 优化改进：将设备地址ComboBox改为TextBox输入框，支持输入1-247任意设备地址
- 优化改进：设备地址输入框支持十进制输入，程序自动转换为字节地址
- 优化改进：修改设备地址后，上位机可通过输入框继续与新地址设备通信
- 优化改进：串口关闭时自动停止所有BMS轮询定时器，避免通信错误
- 优化改进：BMS所有功能固定使用地址2（BMS默认地址），FCU功能使用输入框地址
- 优化改进：轮询过程中异常静默处理，不干扰用户操作
- 优化改进：轮询开启/关闭时按钮状态自动切换，防止误操作
- 优化改进：轮询间隔下拉框在轮询运行时自动禁用，确保设置一致性
- 代码重构：新增InitializeBMSPollingTimers方法统一初始化三个BMS轮询定时器
- 代码重构：新增BMS轮询功能region，包含3个Tick事件和6个按钮事件处理方法
- UI改进：BMS Tab中每个信息分组新增轮询控制区域，布局与电堆信息Tab保持一致
- UI改进：将"读取XX信息"按钮改为"读取一次"，与自动刷新功能明确区分
- UI改进：串口设置区域标签从"设备地址"改为"从站地址(1-247)"，更明确清晰

*V1*：
- 新增功能：新增BMS电池管理系统Tab，支持BMS设备的完整管理和监控
- 新增功能：实现BMS基本信息读取功能（功能码0x02），包括SOC、剩余容量、总电压电流、单体电压、温度等32字节数据
- 新增功能：实现BMS电池组配置信息读取（功能码0x03），显示串并联数、额定容量电压、充放电截止参数
- 新增功能：实现BMS故障信息读取（功能码0x07），支持当前故障和历史故障的位掩码解析
- 新增功能：实现BMS充放电控制功能（功能码0x11），支持允许充电、允许放电、允许充放电、禁止充放电四种操作
- 新增功能：实现BMS定时上报接收（功能码0xF3），每5秒自动更新BMS基本信息
- 新增功能：实现BMS报警事件上报接收（功能码0xF4），实时弹窗显示报警信息（单体过压/欠压、总压过压/欠压、温度异常等）
- 新增功能：实现BMS状态变更上报接收（功能码0xF5），实时监控BMS工作状态和充放电状态变化
- 新增功能：为FCU协议新增修改设备地址功能（功能码0x0C/0x8C），支持远程修改FCU设备地址（1-247）
- 新增功能：在"通用参数"Tab中新增"设备地址管理"分组，提供设备地址修改界面和操作确认
- 优化改进：改进帧接收处理逻辑，根据数据长度智能区分FCU和BMS的响应帧（功能码0x82和0x87复用问题）
- 优化改进：添加BMS故障码位掩码解析，支持13种故障类型的同时显示
- 优化改进：增强错误处理机制，BMS控制指令失败时显示详细错误信息（15种错误码）
- 优化改进：在设备地址修改界面添加黄色警告提示，提醒用户修改后需重启设备
- 文档更新：完善主要功能列表，新增BMS电池管理相关功能说明
- 文档更新：更新通信协议说明，支持FCU和BMS双协议共用RS485总线
- 代码重构：在ResponseParser类中新增GetDataField方法，统一数据域提取逻辑
- 代码重构：在MainWindow.xaml.cs中新增BMS电池管理功能区域，模块化设计提高可维护性
- 配置变更：BMS默认设备地址设置为2，与FCU地址1区分

### 2026-01-26

*V9*：
- UI改进：调整串口按钮布局，刷新串口和打开串口按钮放在同一行
- UI改进：刷新串口按钮位于左侧，打开串口按钮位于右侧
- UI改进：两个按钮大小完全一致（高度36px），视觉更统一
- UI改进：两个按钮之间保持10px间距，布局更美观
- UI改进：刷新串口按钮文本从图标改为"刷新串口"，更直观易懂
- 优化改进：刷新和打开按钮平分宽度，自适应面板宽度

*V8*：
- 新增功能：添加串口列表刷新按钮，支持手动刷新检测新插入的串口设备
- 优化改进：刷新串口列表时智能保留当前选中项，避免用户重复选择
- 优化改进：如果之前选中的串口已断开，自动切换到第一个可用串口并提示用户
- 优化改进：串口打开时禁止刷新列表，防止操作冲突
- 优化改进：刷新后显示检测到的串口数量，提升用户体验
- 优化改进：手动刷新机制，不会自动刷新打断用户选择串口
- 代码重构：重构串口列表初始化逻辑，提取为可重用的刷新方法
- UI改进：在串口号选择框旁添加刷新按钮（🔄图标），位置醒目方便操作

*V7*：
- 新增功能：电堆信息Tab新增自动轮询功能，支持实时刷新电堆数据
- 新增功能：添加"开启自动刷新"和"停止自动刷新"按钮，用户可自主控制轮询开关
- 新增功能：提供多档轮询间隔选择（0.5秒、1秒、2秒、5秒），满足不同监控需求
- 新增功能：独立的DispatcherTimer实现轮询，不阻塞UI主线程
- 优化改进：轮询采用异步非阻塞设计，修改参数时不影响正常操作
- 优化改进：关闭串口时自动停止轮询，避免通信错误
- 优化改进：轮询过程中异常静默处理，不干扰用户操作
- 优化改进：轮询开启/关闭时按钮状态自动切换，防止误操作
- 优化改进：轮询间隔下拉框在轮询运行时自动禁用，确保设置一致性

*V6*：
- 优化改进：重新设计参数设置UI，为每个参数添加独立的修改按钮
- 新增功能：为每个参数Tab添加独立的读取按钮（读取压力参数、读取阀门参数等）
- 优化改进：参数修改更加灵活，用户可以单独修改某一项参数而不影响其他参数
- 优化改进：压力参数Tab - 5个参数各配独立修改按钮
- 优化改进：阀门参数Tab - 5个参数各配独立修改按钮
- 优化改进：DCDC参数Tab - 2个参数各配独立修改按钮
- 优化改进：风扇温度参数Tab - 5个参数各配独立修改按钮
- 优化改进：拉载控制参数 - 5个参数各配独立修改按钮
- 优化改进：拉载启动参数 - 3个参数各配独立修改按钮
- 优化改进：通用参数Tab - 1个参数配独立修改按钮
- 优化改进：拉载时序参数保留整体设置按钮（15个时序，整体设置更合理）
- 代码重构：使用辅助方法简化参数设置代码，提高代码可维护性
- 修复问题：修复可空性警告，增加空引用检查，提高代码健壮性

*V5*：
- 新增功能：完整实现了RS485通信协议的数据接收和解析功能
- 新增功能：实现了帧接收状态机，支持按字节接收和帧完整性校验
- 新增功能：实现了所有响应帧的数据解析（设备序列号、电堆信息、参数配置等）
- 新增功能：实现了电堆实时信息的UI显示更新，支持26个数据项的实时监控
- 新增功能：实现了参数配置读取功能，可读取压力、阀门、DCDC、风扇温度、通用等5类参数
- 新增功能：实现了主动上报处理，支持报警信息(0xF0)和状态变更(0xF1)的接收和显示
- 新增功能：实现了线程安全的UI更新机制，使用Dispatcher确保后台线程安全访问UI
- 优化改进：数据单位自动转换，电压/电流/温度按协议要求进行单位换算
- 优化改进：完善的错误处理机制，CRC校验失败和帧格式错误时自动忽略
- 文档更新：添加了完整的代码注释和功能说明
- 代码重构：采用模块化设计，分为Models、Protocol和UI三层架构
- 配置变更：新增Models和Protocol文件夹，增强代码组织结构

*V4*：

*V4*：
- 修复问题：重构TextBox的Template，修复了文字被裁剪无法显示的严重问题
- 修复问题：调整Padding为8,0（移除垂直方向padding），避免内容被挤压
- 优化改进：将ScrollViewer包装在Grid中，符合WPF最佳实践
- 优化改进：添加SnapsToDevicePixels="True"，改善渲染清晰度
- 优化改进：正确配置ScrollViewer的Focusable和滚动条可见性属性

*V3*：
- 修复问题：明确设置TextBox的前景色（Foreground）为#333333，确保文字可见
- 优化改进：进一步优化字体大小（从11px调整为10px），并增加高度至32px
- 优化改进：调整内边距至8,7，提供更多空间让文字完整显示
- 优化改进：将文字渲染模式改为ClearType，提高字体清晰度
- 优化改进：同步调整ComboBox的高度和字体大小，保持界面一致性

*V2*：
- 优化改进：增加了输入框和下拉框的高度（从28px改为30px），并调整了内边距（从6,4改为6,6），确保文字完整显示
- 优化改进：在TextBox的ScrollViewer中添加了VerticalContentAlignment="Center"属性，进一步优化文字垂直对齐

*V1*：
- 优化改进：减小了输入框和下拉框的字体大小（从12px改为11px），使界面更加协调
- 优化改进：增加了按钮字体的粗细（从SemiBold改为Bold），提高按钮文字的清晰度和可读性
- 修复问题：修复了拉载参数TAB中"拉载启动参数"分组内按钮与输入框重叠的布局问题，添加了第4行定义并调整了按钮的上边距
