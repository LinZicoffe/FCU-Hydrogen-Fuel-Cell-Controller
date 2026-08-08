# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# 请每次回答称呼我为东哥

## 强制规则：推送到 GitHub 前必须更新 README.md

**每次执行 `git push` 推送到 GitHub 之前，必须先更新 `README.md` 文档。**
- 更新内容：反映本次推送涉及的代码变更（新功能、BUG修复、重构等）
- 更新位置：主要更新「更新日志」部分，在对应日期下添加变更条目
- 如有重大功能变更，同步更新「功能概览」「项目结构」等相关章节
- **此规则不可跳过**，即使变更很小也必须在 README 中体现

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

- **Code-behind**：按 `#region` 划分为以下部分（用 `grep -n "#region" MainWindow.xaml.cs` 查看当前行号）

  | region | 职责 |
  |---|---|
  | 初始化方法 | 服务构建、事件挂接、定时器与图表初始化 |
  | 串口操作 | 打开/关闭串口，含 0x0D 总线接管与归还 |
  | 读取操作 | 序列号、电堆信息读取 |
  | 系统控制 | 启动/关闭电堆 |
  | 电堆信息轮询 | `_pollingTimer` 周期读 0x07 |
  | 读取参数按钮 | 各参数组的 0x0A 读取入口 |
  | 参数设置 | 各功能码的帧构建与下发 |
  | 参数前端校验 | `TryParseRange()` / `ParseRequiredField()`，对齐固件校验规则 |
  | 拉载手动控制 | 0x0504 的 8 条控制指令 |
  | 通信协议辅助方法 | 组帧、CRC、日志 |
  | 串口数据接收处理 | 字节流并行喂入 AA55 与 Modbus 两个解析器 |
  | 响应处理方法 | 各响应码的分发与处理 |
  | BMS 总线仲裁 | 0x0D 使能收发与确认等待 |
  | UI更新方法 | ViewModel 与控件刷新 |
  | BMS电池管理功能 | Modbus 数据展示、MOS 控制、拉载阻断横幅 |
  | 窗口事件 | 关闭前归还总线、日志复制等 |
  | 拉载配方管理 | 配方增删改查与应用到设备 |

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

**协议文档与固件源码位于下位机仓库** `D:\C_C++text\450V_FCU_git`：

- FCU 协议：`docs/01-协议规范/02-FCU 氢燃料电池控制器 RS485 通信协议.md`
- 固件实现（**判断协议行为的唯一标准**）：`App/Src/fcu_protocol.c`、`App/Inc/fcu_protocol.h`
- 状态机与告警逻辑：`App/Src/fcu_control.c`
- BMS Modbus 主站：`App/Src/fcu_modbus.c`

> 协议文档与固件注释都可能滞后于代码。核对协议行为时**以固件 `.c` 文件的实际实现为准**。

### 协议版本信息（当前）

| 协议文档 | 当前版本 | 修订日期 | 关键变更 |
|---|---|---|---|
| FCU 通信协议 | V4.3 | 2026-02-05 | 补充 4.9 读取配置参数响应帧描述（数据域含 param_id 2B + sub_index 1B） |
| FCU 通信协议 | V4.2 | 2026-02-02 | 压力参数 12字节→16字节，新增高压/低压量程 |
| BMS 通信协议 | V1.28-S | - | Modbus RTU，寄存器映射见协议文档附表 |

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

### BMS 通信方式（Modbus RTU）

**BMS 不使用 AA55 自定义协议，而是标准 Modbus RTU。** 上位机作为 Modbus 主站直接与 BMS 通信。

| 功能码 | 描述 | 实现位置 |
|--------|------|---------|
| 0x03 | 读保持寄存器（读取全部 BMS 数据） | `Protocol/ModbusRtu.cs`、`Services/BmsService.cs` |
| 0x10 | 写单个寄存器（充放电 MOS 控制） | `BmsService.SetChargeMos()` / `SetDischargeMos()` |

- **寄存器范围**：上位机读 128~152（25个），下位机固件读 130~141（12个，仅需 SOC 与状态位图）
- **位图解码**：reg137 告警 / reg138 保护 / reg139 故障 / reg140 系统状态，见 `Models/BmsBitDecoder.cs`
- **通信参数**：响应超时 300ms、总线静默阈值 20ms、连续 3 次超时判离线

> 历史说明：旧版曾把 BMS 挂在 AA55 协议下（功能码 0x02/0x03/0x07/0x11/0xF3/0xF4/0xF5），
> 已全部废弃并改为 Modbus RTU。相关代码见 `MainWindow.xaml.cs` 的删除标记注释。

### 设备地址分配

- **FCU**：默认地址1（可配置1-247，功能码 0x0C 修改，需重启生效）
- **BMS**：默认地址1（UI 可配，与固件 `FCU_MODBUS_SLAVE_ADDR` 一致）

### 总线仲裁（0x0D）

FCU 与上位机都可作为 BMS 的 Modbus 主站，需避免双主站冲突：

- 上位机打开串口时发 `0x0D en=0` 接管总线（停固件轮询）
- 关闭串口时发 `0x0D en=1` 归还，并等 0x8D 确认
- **固件把该使能位持久化到 Flash**：上位机异常退出会残留 `en=0`，导致 FCU 重启后仍不轮询 BMS。
  BMS Tab 提供「归还FCU轮询」按钮用于手动恢复
- 上位机侧发 Modbus 请求前的三条件仲裁（与固件 `fcu_modbus.c` 对齐）：
  串口已打开 + AA55 接收状态机空闲（`ProtocolHandler.IsIdle`）+ 总线静默 ≥20ms

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

### BMS 轮询（Modbus RTU）

由 `Services/BmsService.cs` 统一管理，单个 `DispatcherTimer` 周期发 03H 读取全部寄存器：

- 轮询间隔 UI 可配（默认 2.0s，与固件 `tick_2s` 一致）
- 发请求前做三条件仲裁：串口已打开 + AA55 状态机空闲 + 总线静默 ≥20ms；不满足则推迟 30ms 重试一次，仍忙放弃本轮
- 响应超时 300ms，连续 3 次超时判为离线并清空显示
- 离线时会提示「下位机侧 SOC 门控与 BMS 故障耦合已失效」——这是安全相关的降级状态

> 旧版曾用三个独立定时器分别轮询 AA55 的 0x02/0x03/0x07，已废弃。

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
4. **状态机**：协议处理使用状态机模式，确保帧接收的可靠性。含 1000ms 帧间超时自愈（与固件 `FCU_RS_485_FRAME_TIMEOUT_MS` 一致），防止残帧吞掉后续帧的起始字节
5. **错误处理**：所有解析方法都包含 try-catch。但**帧分发与响应处理不可静默吞异常**，否则表现为"界面某几个框不更新"却无任何线索
6. **参数设置模式**：FCU 协议的参数设置（如功能码 0x02）要求**发送完整的参数数据域**，而非仅发送变更的单个参数。例如设置压力参数时，即使只修改"高压最小值"，也必须发送所有 16 字节的完整数据（含参数类型ID、高低压阈值、量程等）。
7. **双协议隔离**：FCU 走 AA55 自定义协议，BMS 走 Modbus RTU，两者在同一串口上并行喂入两个独立解析器
   （`ProtocolHandler.ProcessReceivedByte()` 与 `BmsService.AppendReceivedBytes()`）。
   AA55 帧必须以 `AA 55` 开头，Modbus 帧靠 `地址+功能码+长度+CRC` 三级过滤，**不存在响应码复用歧义**。
8. **响应长度校验**：`ResponseParser` 的各 `Parse*Params` 在长度不符时会静默返回全 0 对象。
   UI 侧必须先调 `TryValidateReadConfigResponse()` 校验，否则用户会看到一片 0 却被告知"读取成功"。
   长度表集中在 `ResponseParser.GetExpectedParameterDataLength()`，与固件 `fcu_handle_read_config()` 各分支 `data_len` 一一对应。
9. **发送前校验对齐固件**：固件对参数是**整帧校验**，任一字段非法则整组参数都不生效。
   上位机须在发送前用 `TryParseRange()` / `ParseRequiredField()` 拦截并指明具体字段，
   否则用户只能看到笼统的"参数错误"。特别注意压力量程为 0 会被固件**静默接受并写 Flash**，
   导致压力读数恒为 0 且重启不恢复。
10. **默认值以固件 `.c` 为准**：固件 `fcu_protocol.h` 的字段注释存在过期值（旧版附录A），
    UI 默认值必须对齐 `fcu_protocol.c` 中各 `g_*_params` 结构体的实际初始化值。
11. **有符号字段**：电堆温度与 FCU 温度下限为 -40℃，固件按二进制补码塞进 `uint16` 发出，
    上位机必须按 `short` 解析，否则 -40℃ 会显示成 6513.6℃。
12. **报警数值单位随类型而变**：`0xF0` 的 `alarm_value` 在压力类是 KPA 原值、温度类是 ×10、
    电压类是 ×10、`0x0008` 是 BMS 故障位图，需按类型分别换算（见 `AlarmData.GetAlarmValueDescription()`）。
13. **BMS 阻断拉载**：固件 `FCU_BMS_PROTECT_CRITICAL_MASK = 0x537F` 命中或 MOS 关断时会拒绝拉载，
    但**是静默 return，0x82 仍回 0x00 成功**。上位机靠 `BmsBitDecoder.IsLoadBlocked()` 主动前置告警，
    做不到事后确认。根治需固件回传错误码。

14. **SOC 门控不可查询**：固件硬编码 SOC>90 拒启、SOC>85 运行中自动停机，协议层无法读取这两个阈值。
    启动响应 `0x03` 同时覆盖"低压超区间/温度≥风扇上限/SOC>90"三种原因，无法从响应码区分。

### UI 更新和轮询

15. **UI 更新**：使用辅助方法简化参数设置代码，保持代码可维护性
16. **轮询机制**：
    - 使用 `DispatcherTimer` 实现异步轮询
    - 关闭串口时自动停止所有轮询定时器
    - 轮询定时器与图表刷新中出错静默处理（避免每 100ms 弹提示），但帧分发不适用此约定

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

## 项目文件结构

```
FCU 氢燃料电池控/
├── FCU 氢燃料电池控.sln           # 解决方案文件
├── README.md                        # 项目说明和更新日志
├── UI设计风格文档.md                 # UI 设计规范
├── 许可证系统使用指南.md             # 许可证使用指南
├── 打包教程.md                       # 打包发布说明
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
    ├── App.xaml / App.xaml.cs      # 应用程序定义、启动时许可证验证
    ├── MainWindow.xaml             # 主窗口 UI
    ├── MainWindow.xaml.cs          # 主窗口逻辑
    ├── Models/                     # 数据模型层
    │   ├── ProtocolModels.cs       # FCU 协议数据模型
    │   ├── BmsData.cs              # BMS 数据模型
    │   ├── BmsBitDecoder.cs        # BMS 位图解码 + 拉载阻断判定
    │   ├── RecipeModels.cs         # 拉载配方模型
    │   ├── CommunicationLogEntry.cs # 通信日志条目
    │   ├── LicenseModels.cs        # 许可证数据模型
    │   └── RegistrationModels.cs   # 注册数据模型
    ├── Protocol/                   # 协议处理层
    │   ├── ProtocolHandler.cs      # AA55 帧状态机（含超时自愈）
    │   ├── ResponseParser.cs       # 响应解析 + 长度校验表
    │   ├── ProtocolConstants.cs    # 功能码、参数ID、错误码常量
    │   └── ModbusRtu.cs            # Modbus RTU 组帧与解析
    ├── Services/                   # 业务服务层
    │   ├── BmsService.cs           # BMS Modbus 主站（含总线仲裁）
    │   ├── RecipeService.cs        # 拉载配方持久化
    │   ├── LicenseService.cs       # 许可证验证服务
    │   ├── RegistrationService.cs  # 注册服务
    │   └── EncryptionService.cs    # 加密辅助服务
    ├── Managers/                   # 管理器层
    │   ├── SerialPortManager.cs    # 串口生命周期管理
    │   └── ParameterManager.cs     # 参数下发管理
    ├── ViewModels/                 # MVVM 视图模型
    │   └── MainViewModel.cs        # 主窗口 VM（含压力参数校验）
    ├── Helpers/                    # 辅助类
    │   ├── ObservableObject.cs     # 属性变更通知基类
    │   ├── RelayCommand.cs         # 命令实现
    │   ├── SerialPortHelper.cs     # 串口校验辅助
    │   └── UIUpdater.cs            # UI 更新辅助
    ├── Converters/                 # XAML 值转换器
    ├── Utils/                      # 工具类库
    │   ├── CryptoHelper.cs         # 加密辅助工具
    │   ├── HardwareInfo.cs         # 硬件信息获取
    │   └── SecureStorage.cs        # 安全存储
    └── Views/                      # 视图层
        ├── LicenseWindow.xaml      # 许可证激活窗口
        ├── RegistrationWindow.xaml # 注册窗口
        └── RecipeNameDialog.xaml   # 配方命名对话框
```
