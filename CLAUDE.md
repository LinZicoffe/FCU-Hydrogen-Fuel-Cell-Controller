# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# 请每次回答称呼我为东哥

## 项目概述

这是一个基于 WPF (.NET 8.0) 开发的工业控制软件，用于通过 RS485 接口与氢燃料电池控制器（FCU）和电池管理系统（BMS）进行通信。

## 构建和运行

```bash
# 构建整个解决方案
dotnet build "FCU 氢燃料电池控.sln"

# 运行主项目
dotnet run --project "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj"

# 发布 Release 版本
dotnet publish "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj" -c Release -o ./publish

# 构建许可证生成工具
dotnet build "Tools/LicenseKeyGenerator/LicenseKeyGenerator.csproj"

# 运行许可证生成工具
dotnet run --project "Tools/LicenseKeyGenerator/LicenseKeyGenerator.csproj"

# 构建试用数据清除工具
dotnet build "Tools/ClearTrialData/ClearTrialData.csproj"

# 运行试用数据清除工具
dotnet run --project "Tools/ClearTrialData/ClearTrialData.csproj"
```

## 核心架构

项目采用三层架构设计：

### 1. Models 层 (`FCU 氢燃料电池控/Models/`)

数据模型定义，包含协议数据结构和许可证模型：
- `ProtocolModels.cs` - 包含所有协议数据结构
  - `StackInfoData` - 电堆信息（26字节）
  - `PressureParams` - 压力参数（V4.2协议：16字节，包含高压量程和低压量程）
  - `ValveParams` - 阀门参数
  - `DCDCParams` - DCDC参数
  - `FanTempParams` - 风扇温度参数
  - `AlarmData` - 报警信息
  - `StateChangeData` - 状态变更数据
  - `GeneralParams` - 通用参数
- `LicenseModels.cs` - 许可证系统数据模型
  - `LicenseData` - 许可证数据结构（含 RSA 签名）
  - `TrialInfo` - 试用信息
  - `LicenseValidationResult` - 验证结果
  - `LicenseType` - 许可证类型枚举
  - `LicenseFeatures` - 功能权限标志位
  - `LicenseStatus` - 许可证状态枚举

### 2. Protocol 层 (`FCU 氢燃料电池控/Protocol/`)

协议处理核心逻辑：

- **`ProtocolHandler.cs`** - 协议处理器
  - 实现帧接收状态机（Idle → Header1 → Header2 → Address → FunctionCode → DataLength → Data → CRC_High → CRC_Low）
  - CRC16-CCITT 校验计算
  - 帧完整性验证
  - 通过事件 `FrameReceived` 和 `FrameValidationFailed` 通知上层
  - 静态方法 `BuildRequestFrame()` 构建请求帧

- **`ResponseParser.cs`** - 响应数据解析器
  - 静态方法解析各种响应帧
  - 处理大端序字节序转换
  - 数据单位转换（0.1V、0.1A、0.1℃ 等）
  - `GetDataField()` - 统一数据域提取方法

### 3. UI 层 (`FCU 氢燃料电池控/MainWindow.xaml` + `MainWindow.xaml.cs`)

- **XAML**：定义 Material Design 风格的用户界面
  - 顶部标题栏（高度50px）
  - 主内容区：左侧固定面板（270px）+ 右侧Tab区域（自适应）
  - 功率曲线图（高度200px，可调整）
  - 底部状态栏（高度25px）
  - GridSplitter分隔条支持调整图表高度

- **Code-behind**（3030行，16个region）：
  1. 初始化方法（约265行）
  2. 串口操作（约122行）
  3. 读取操作（约107行）
  4. 系统控制（约54行）
  5. 电堆信息轮询（约94行）
  6. 读取参数按钮（约153行）
  7. 参数设置（约687行）
  8. 通信协议辅助方法（约67行）
  9. 串口数据接收处理（约132行）
  10. 响应处理方法（约180行）
  11. UI更新方法（约392行）
  12. BMS电池管理功能（约195行）
  13. BMS轮询功能（约456行）
  14. 功率曲线图功能（约152行）
  15. 窗口事件（约25行）

### 4. Services 层 (`FCU 氢燃料电池控/Services/`)

业务服务层：
- **`LicenseService.cs`** - 许可证验证服务
  - 启动时验证许可证状态
  - 支持试用模式（90天试用期）
  - 激活许可证验证（硬件指纹、RSA 签名、过期时间）
  - 时间回退检测
  - 硬件变更检测

- **`EncryptionService.cs`** - 加密辅助服务
  - 字符串混淆/反混淆
  - 随机盐值生成
  - 文件哈希计算（SHA256）
  - 随机字符串生成

### 5. Utils 层 (`FCU 氢燃料电池控/Utils/`)

工具类库：
- **`CryptoHelper.cs`** - 加密辅助工具
  - RSA 签名验证（2048位密钥，SHA256）
  - AES-256 加密/解密（用于安全存储）
  - PBKDF2 密钥派生
  - SHA256 哈希计算

- **`HardwareInfo.cs`** - 硬件信息获取
  - 硬件指纹生成：`SHA256(CPU_ID + Motherboard_Serial + MAC_Address + Disk_Serial)`
  - 使用 WMI 获取硬件信息
  - 降级方案：使用 MachineName + UserName

- **`SecureStorage.cs`** - 安全存储
  - 使用 `IsolatedStorage` 存储加密数据
  - AES 加密许可证和试用信息
  - 用户级隔离

### 6. Views 层 (`FCU 氢燃料电池控/Views/`)

- **`LicenseWindow.xaml`** - 许可证激活窗口

## 通信协议要点

完整的协议定义参见 [FCU 氢燃料电池控制器 RS485 通信协议.md](FCU 氢燃料电池控制器 RS485 通信协议.md) 和 [BMS电池管理系统RS485通信协议.md](BMS电池管理系统RS485通信协议.md)。

### 协议版本信息（当前）

| 协议文档 | 当前版本 | 修订日期 | 关键变更 |
|---|---|---|---|
| FCU 通信协议 | V4.2 | 2026-02-02 | 压力参数 12字节→16字节，新增高压/低压量程 |
| BMS 通信协议 | - | - | 参见协议文档 |

**协议变更影响代码的典型位置：**
- `Models/ProtocolModels.cs` - 数据结构定义
- `Protocol/ResponseParser.cs` - 响应数据解析
- `MainWindow.xaml` - UI 控件定义
- `MainWindow.xaml.cs` - 参数设置、UI更新方法

### 通用帧结构

```
| 帧头(2) | 地址(1) | 功能码(1) | 长度(1) | 数据域(N) | CRC16(2) |
| 0xAA 0x55 |   |   |   |   |   |
```

帧总长度 = 7 + N 字节

### FCU 关键功能码

| 功能码 | 描述 | 响应码 |
|--------|------|--------|
| 0x01 | 读取设备序列号 | 0x81 |
| 0x02 | 设置压力参数 | 0x82 |
| 0x03 | 设置阀门参数 | 0x82 |
| 0x04 | 设置拉载参数 | 0x82 |
| 0x05 | 设置DCDC参数 | 0x82 |
| 0x06 | 设置风扇温度参数 | 0x82 |
| 0x07 | 读取电堆信息 | 0x87 |
| 0x08 | 启动电堆 | 0x88 |
| 0x09 | 关闭电堆 | 0x89 |
| 0x0A | 读取配置参数 | 0x8A |
| 0x0B | 设置通用参数 | 0x82 |
| 0x0C | 修改设备地址 | 0x8C |

### FCU 主动上报功能码

| 功能码 | 描述 |
|--------|------|
| 0xF0 | 报警信息上报 |
| 0xF1 | 状态变更上报 |
| 0xF2 | 拉载状态上报 |

### BMS 关键功能码

| 功能码 | 描述 |
|--------|------|
| 0x02 | 读取基本信息（32字节） |
| 0x03 | 读取电池组配置 |
| 0x07 | 读取故障信息 |
| 0x11 | 充放电控制 |
| 0xF3 | 定时上报 |
| 0xF4 | 报警事件上报 |
| 0xF5 | 状态变更上报 |

### 设备地址分配

- **FCU**：默认地址1（可配置1-247）
- **BMS**：固定地址2

## 功率曲线图功能

### 技术实现

- **图表库**：LiveChartsCore.SkiaSharpView.WPF (v2.0.0-rc2)
- **渲染引擎**：SkiaSharp.Views.WPF (v2.88.8)

### 功能特性

- **双曲线显示**：
  - 输入功率（蓝色 #2196F3）= DCDC输入电流 × DCDC输入电压
  - 输出功率（橙红色 #FF5722）= DCDC输出电流 × DCDC输出电压
- **数据采集**：10 Hz（100ms/点）
- **时间窗口**：1000秒历史数据（最大10000个点）
- **平滑曲线**：LineSmoothness = 0.5
- **自定义图例**：XAML手动创建，避免字体兼容性问题
- **位置**：程序下方，不受Tab切换影响，始终可见

## 自动轮询功能

### 电堆信息轮询

- 使用 `DispatcherTimer` 实现（`_pollingTimer`）
- 可配置间隔（0.5秒、1秒、2秒、5秒）
- 关闭串口时自动停止
- 静默错误处理

### BMS三模块轮询

三个独立的 `DispatcherTimer`：
1. **基本信息轮询**：功能码0x02，32字节数据
2. **电池组配置轮询**：功能码0x03
3. **故障信息轮询**：功能码0x07

每个模块支持独立开启/停止和轮询间隔配置。

## UI 设计规范

完整的 UI 设计规范参见 [UI设计风格文档.md](UI设计风格文档.md)。

### 核心色彩

```csharp
PrimaryColor   #1976D2  // 主蓝色
AccentColor    #2196F3  // 强调蓝
DarkColor      #0D47A1  // 深蓝色
SuccessColor   #4CAF50  // 成功绿
WarningColor   #FF9800  // 警告橙
DangerColor    #F44336  // 危险红
```

### 设计原则

- **KISS 原则**：界面简洁直观
- **视觉一致性**：统一的色彩、字体、圆角、阴影规范
- **即时反馈**：所有交互都有明确的视觉状态变化
- **性能优先**：使用 BitmapCache 等优化技术

## 依赖项

- `System.IO.Ports` (v10.0.2) - 串口通信
- `System.Management` (v8.0.0) - WMI 硬件信息获取
- `LiveChartsCore.SkiaSharpView.WPF` (v2.0.0-rc2) - 功率曲线图表库
- `SkiaSharp.Views.WPF` (v2.88.8) - SkiaSharp 图表渲染引擎

## 开发注意事项

### 通信协议相关

1. **线程安全**：UI 更新必须通过 `Dispatcher.Invoke()` 或 `Dispatcher.BeginInvoke()` 进行
2. **字节序**：协议使用大端序（Big-Endian），解析时需要注意字节顺序
3. **单位转换**：许多参数使用缩放单位（0.1V、0.1A、0.1℃），显示时需要转换
4. **状态机**：协议处理使用状态机模式，确保帧接收的可靠性
5. **错误处理**：所有解析方法都包含 try-catch，避免通信异常导致程序崩溃
6. **参数设置模式**：FCU 协议的参数设置（如功能码 0x02）要求**发送完整的参数数据域**，而非仅发送变更的单个参数。例如设置压力参数时，即使只修改"高压最小值"，也必须发送所有 16 字节的完整数据（含参数类型ID、高低压阈值、量程等）。
7. **BMS协议区分**：
   - 功能码0x82和0x87在FCU和BMS协议中复用
   - 通过数据长度智能区分（FCU参数响应2字节，BMS基本信息响应32字节）

### UI 更新和轮询

8. **UI 更新**：使用辅助方法简化参数设置代码，保持代码可维护性
9. **轮询机制**：
   - 使用 `DispatcherTimer` 实现异步轮询
   - 关闭串口时自动停止所有轮询定时器
   - 轮询过程中出错静默处理，不影响主程序运行

## 许可证系统

### 启动流程

应用启动时（`App.xaml.cs`）会执行许可证验证：

1. **首次运行**：初始化 90 天试用期，显示欢迎窗口
2. **试用中**：验证试用状态，到期前 7 天显示警告
3. **试用过期**：强制显示激活窗口，无法继续使用
4. **已激活**：验证许可证有效性（硬件指纹、RSA 签名、过期时间）
5. **验证失败**：根据错误类型显示相应提示并退出

### 许可证验证机制

- **硬件绑定**：许可证与硬件指纹（SHA256）绑定，无法跨设备使用
- **RSA 签名**：2048位 RSA 密钥对，私钥签名、公钥验证
- **时间检测**：检测系统时间回退，防止通过修改系统时间延长试用
- **加密存储**：使用 AES-256 加密存储在 IsolatedStorage

### 许可证生成工具

位于 `Tools/LicenseKeyGenerator/`，功能：
- 首次运行自动生成 RSA 密钥对
- 根据客户信息生成许可证文件
- 支持标准版、专业版、企业版
- 支持 1 年、2 年、3 年、永久授权

**重要**：生成工具首次运行会显示 RSA 公钥，必须将其复制到客户端 `Utils/CryptoHelper.cs` 中的 `RSA_PUBLIC_KEY` 常量。

### 试用数据清除工具

位于 `Tools/ClearTrialData/`，用于开发测试时清除 IsolatedStorage 中的试用数据。

### 开发时许可证调试

开发调试时如遇到硬件指纹不匹配问题，可使用以下方法：

1. **使用清除工具**（推荐）：
   ```bash
   dotnet run --project "Tools/ClearTrialData/ClearTrialData.csproj"
   ```

2. **禁用许可证验证**（开发环境）：
   在 `App.xaml.cs` 中使用 `#if DEBUG` 条件编译跳过许可证验证

3. **详细调试指南**：参见 [开发调试指南.md](开发调试指南.md)

## 项目文件结构

```
FCU 氢燃料电池控/
├── FCU 氢燃料电池控.sln           # 解决方案文件
├── README.md                        # 项目说明和更新日志
├── UI设计风格文档.md                 # UI 设计规范
├── 开发调试指南.md                   # 开发调试指南
├── 许可证系统使用指南.md             # 许可证使用指南
├── FCU 氢燃料电池控制器 RS485 通信协议.md  # FCU通信协议文档
├── BMS电池管理系统RS485通信协议.md          # BMS通信协议文档
├── JT808-2019 多设备指令透传扩展通信协议设计.md  # T-Box协议文档
├── CLAUDE.md                        # 本文件
├── Tools/                           # 工具项目
│   ├── LicenseKeyGenerator/         # 许可证生成工具
│   │   ├── LicenseKeyGenerator.csproj
│   │   └── Program.cs
│   └── ClearTrialData/              # 试用数据清除工具
│       ├── ClearTrialData.csproj
│       └── Program.cs
└── FCU 氢燃料电池控/
    ├── FCU 氢燃料电池控.csproj     # 项目文件
    ├── App.xaml                    # 应用程序定义
    ├── App.xaml.cs                 # 启动时许可证验证
    ├── MainWindow.xaml             # 主窗口 UI
    ├── MainWindow.xaml.cs          # 主窗口逻辑
    ├── Models/                     # 数据模型层
    │   ├── ProtocolModels.cs       # 协议数据模型
    │   └── LicenseModels.cs        # 许可证数据模型
    ├── Protocol/                   # 协议处理层
    │   ├── ProtocolHandler.cs      # 协议处理器
    │   └── ResponseParser.cs       # 响应解析器
    ├── Services/                   # 业务服务层
    │   ├── LicenseService.cs       # 许可证验证服务
    │   └── EncryptionService.cs    # 加密辅助服务
    ├── Utils/                      # 工具类库
    │   ├── CryptoHelper.cs         # 加密辅助工具
    │   ├── HardwareInfo.cs         # 硬件信息获取
    │   └── SecureStorage.cs        # 安全存储
    └── Views/                      # 视图层
        └── LicenseWindow.xaml      # 许可证激活窗口
```
