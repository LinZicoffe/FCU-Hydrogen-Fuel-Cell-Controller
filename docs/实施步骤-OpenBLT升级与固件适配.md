# 实施步骤：OpenBLT 在线升级功能 + 固件 2026-09-17 更新适配

> 适用对象：上位机《FCU 氢燃料电池控》（.NET 8 WPF，`D:\C#_text\WPF\FCU 氢燃料电池控\FCU 氢燃料电池控\`）
> 固件依据：450V_FCU `beforepined` 分支 @ `51bf7e2`（2026-09-17 更新）；升级适配参照 `codex/openblt-app-upgrade` 分支（**注意：其尖端 abc0115 存在合并回退，联调须用 c5fcd2e 或修复后产物，见 §3.3 与待确认②**）
> 引导依据：Boot-For-FCU @ `2a3904b`（OpenBLT 移植，私有仓库 `D:\C_C++text\450V_FCU_before_pin_change\Boot-For-FCU`）
> 约束：不修改任何嵌入式代码；每步修改需在嵌入式源码中有对应依据；每步完成后交独立 Agent 审查、通过后本地 git 提交。

## 1. 项目目标

固件侧发生两件大事，上位机需要同步：

1. **引导方案换代**：官方已于 2026-09-20 重置 main 撤销旧自研 BL/IAP；新方案为 Boot-For-FCU（OpenBLT 移植）。上位机现有升级页针对旧自研 BL 协议（.upg 包、APP@0x08004000、9600bps、AA55 0x11 进入）已全部失效，需按 OpenBLT XCP 协议重写在线升级功能。
2. **固件 2026-09-17 更新**改变多处协议默认值与语义（beforepined `3219702`→`51bf7e2`），上位机存在 5 处偏差需修正。

最终效果：用户在上位机固件升级页选择 codex/openblt-app-upgrade 分支构建出的 S-record 固件文件，通过"加载指令/断电重启"使 FCU 进入 OpenBLT，以 57600bps XCP 协议完成擦除、写入、校验、重启全流程，全程有进度与错误提示；其余 5 项协议适配与固件新语义一致。

## 2. 实施范围

### 本次包含

- **A. 新增升级功能（重写升级链路）**
  - S-record（Motorola S1/S2/S3）固件文件解析与合法性校验
  - XCP over RS485 主机端协议实现（C# 原生，不引入第三方库）
  - "加载指令"发送（`XX 99 88 77 + CRC`，XX=设备地址；固件侧适配中，格式待对齐）→ 无应答时回退"断电重启"人工路径
  - 波特率切换（9600↔57600）、擦除、分块写入、结束固化、重启验证、失败恢复与退出归一化
  - 旧 .upg 升级路径 UI 下线（代码保留不删）
- **B. 固件更新适配 5 项**
  - B1 SOC 门控参数：默认值对齐 60/61/60、新增 `start_max < stop_high` 交叉校验、提示文案更新
  - B2 压力参数：UI 默认值 300/30/80 → 100/35/65、ToolTip 同步
  - B3 拉载控制 0x0502 锁定系数：固件已改保留字段，置灰 + 标注（复用阀门页做法）
  - B4 拉载时序表：UI 默认值对齐固件 14 条（第 6/7 行时间 0.70→0.75、第 15 行清空）、组包数量动态、回读回填清空多余行
  - B5 拉载手动控制错误反馈：从状态栏一行文本升级为 Growl 弹窗（对齐风扇 0x8F 处理方式）

### 本次不包含

- 不修改任何嵌入式代码（Boot-For-FCU、450V_FCU 两个仓库只读；"加载指令"的固件侧适配由固件同事另行完成）
- 不做 Bootloader 自身的烧录/升级（BL 位于 0x08000000，XCP 只能写 0x08003000 起，BL 只能 SWD 烧录）
- 不做拉载时序表 UI 从 15 行扩到 64 行（维持既有决策；固件 MAX_LOAD_SEQUENCES=64 不变）
- 不删除旧升级相关代码（`BlProtocolHelper.cs`/`BlUpgFile.cs`/`BlUpgradeClient.cs` 保留在仓库，仅下线 UI 入口与禁用横幅）
- 不做版本号升级与安装包重打（另行安排）
- 不做 .upg 与 XCP 双协议并存（用户已决策：仅新 XCP）

## 3. 当前项目现状

### 3.1 升级页现状（全部针对旧自研 BL，已禁用）

- 禁用横幅：`Views/FirmwareUpgradeView.xaml:24-26`（文案称需 main/V5.2 固件）；开关 `Views/FirmwareUpgradeView.xaml.cs:57` `UpgradeDisabledForCurrentFirmware = true`，构造函数 81-85 禁用按钮，`BtnStart_Click` 257-264 防御拦截。
- 旧升级流程 `RunUpgradeAsync`（`FirmwareUpgradeView.xaml.cs:317-655`）九步：S1 AA55 0x07 探活（仅 OFF/FAULT 允许）→ S1.5 AA55 0x11 进 BL（379-406，无应答回退人工断电）→ S2 30s 轮询 BL GetUid → S3 UID/版本校验 → S4 擦除 → S5 1024B 分块写入（APP_BASE=0x08004000）→ S6 CRC32 比对 → S7 写元数据+复位 → S8 重启验证 → S9 失败退出归一化（`ExitNormalizationAsync` 660-701）。
- 文件格式：仅 `.upg`（`FirmwareUpgradeView.xaml.cs:235`），`Models/BlUpgFile.cs` 解析 'UFW1' 私有格式。
- 旧协议帧：`Protocol/BlProtocolHelper.cs:12-13`（`AA 55 + Cmd(1B) + Len(2B 小端) + Data + Checksum(2B 小端)`），与 OpenBLT XCP 完全不同。
- 串口架构（可复用）：`Managers/SerialPortManager.cs` 唯一串口实例；`MainWindow.xaml.cs:2390-2399` `_blUpgradeMode` 分流（升级模式字节直达 `FirmwareUpgradeView.FeedSerialByte`）；桥接 `BlHostBridgeImpl`（`MainWindow.xaml.cs:4490-4636`：`ReopenSerial` 波特率切换 4527、`SetBlMode` 4564、`SendAppFrame` 4559、`NormalizeExit` 4611）。
- UI 反馈设施（可复用）：`Progress()` 171-179、`Status()` 169 /`Log()` 160-167、五步骤指示 `BrushesStub.StepState()` 128-142。
- 波特率常量：`FirmwareUpgradeView.xaml.cs:60-61` `BL_BAUD=9600, APP_BAUD=9600`（需改 57600）。

### 3.2 引导（Boot-For-FCU）关键事实 —— 上位机协议实现依据

| 项 | 值 | 源码依据 |
|---|---|---|
| 总线 | USART1 PA9/PA10 + PB3=RS485_EN，与 APP 同一条 485 | `Boot_G4.ioc` |
| 波特率 | **57600**，8N1 | `App/blt_conf.h:98`（提交 2a3904b 刚改） |
| 帧格式 | `[len:1B][XCP命令/响应字节...]`，**无校验字节** | `Source/ARMCM4_STM32G4/rs232.c:150-279`、`plausibility.h:258` CS_TYPE=0 |
| 最大包长 | CTO 收/发均 129B | `plausibility.h:93,97` |
| 字节序 | Intel（小端）：SET_MTA 地址、PROGRAM_CLEAR 长度 | `blt_conf.h:51` MOTOROLA=0、`xcp.c:754` |
| 安全认证 | SEED/KEY **禁用**（无需解锁序列） | `blt_conf.h` `BOOT_XCP_SEED_KEY...`、`xcp.h:94-96` |
| 联机命令 | CONNECT `0xFF`：请求 `[FF, mode]`，响应 `[FF, res=0x10, commMode=0x00, maxCto=129, maxDto(2B), ver, ver]` 8B | `xcp.c:552-631` |
| 后门窗口 | 复位后 **500ms** 内收到 CONNECT 即驻留 BL | `Source/backdoor.c:45` |
| 编程命令 | SET_MTA `0xF6`（addr 4B 小端，MTA 自动递增）；PROGRAM_START `0xD2`（响应 7B）；PROGRAM_CLEAR `0xD1`（mode 3B 保留 + size 4B 小端，从 MTA 起擦除）；PROGRAM `0xD0`（[D0, N≤127, data…]；**N=0 表示编程结束**→触发 NvmDone）；PROGRAM_MAX `0xC9`（128B 定长）；PROGRAM_RESET `0xCF`（**直接启动新 APP，不复位 MCU**） | `xcp.c:748-758, 1300-1580` |
| 响应标识 | 成功首字节 `0xFF`；错误 `[FE, errCode]`，errCode：0x00 CMD_SYNCH / 0x10 BUSY / 0x20 UNKNOWN / 0x22 OUT_OF_RANGE / 0x25 ACCESS_LOCKED / 0x29 SEQUENCE / 0x31 GENERIC | `xcp.h:142-162` |
| APP 有效判定 | BL 在编程结束时计算前 7 个异常向量的二补码和，写入 APP 向量表末尾偏移 **0x1D8** 处（该位置在 APP 镜像中为占位字 0x55AA11EE）；每次上电校验该字，无效则驻留 BL | `flash.c:58-59, 384-449`；codex 分支 `startup_stm32g474cbtx.s` 表末新增 `.word 0x55AA11EE` |
| 可擦写区 | 仅 0x08003000–0x0801EFFF（28×4K，BL 区 0x08000000-0x08002FFF 与固件参数页 0x0801F000 均不在表内，**永不会被误擦**；即在线升级保留设备参数页） | 实际生效布局 `Source/ARMCM4_STM32G4/flash_layout.inc`（`blt_conf.h:136` CUSTOM_LAYOUT=1 启用；`flash.c:163-209` 为未启用的默认表，勿参照） |
| 读回能力 | UPLOAD（`0xF5`）可用（XCP_UPLOAD_EN 默认 1，`plausibility.h:668-670`，blt_conf.h 未覆写），可按 MTA 读 Flash 用于写后校验 | `xcp.c:768-824`、`plausibility.h:668-670` |
| 看门狗 | 升级全程 BL 自行喂 IWDG | `App/hooks.c:103-113` |

### 3.3 固件侧（codex/openblt-app-upgrade 分支）配套变化与**当前阻塞状态**

- APP 链接基址 `0x08000000+124K` → `0x08003000+112K`（提交 a8aac47 改 `STM32G474CBTX_FLASH.ld`）
- `Core/Src/main.c` 新增 VTOR 重定位（SCB->VTOR = 向量表链接地址）
- 构建启用 S-record 输出（提交 c5fcd2e）→ 升级文件 = 该分支构建产物 `.srec`
- **⚠ 分支尖端 abc0115（"Merge beforepined"，2026-09-21）在合并冲突解决时把 `STM32G474CBTX_FLASH.ld` 的 MEMORY 块整块还原为 beforepined 侧**：实测 `git show abc0115:STM32G474CBTX_FLASH.ld:49` 为 `FLASH ORIGIN=0x8000000, LENGTH=124K`（RAM 96K 为正确修复），而其父提交 c5fcd2e 仍为 `0x08003000/112K`。同提交中 VTOR 重定位与启动文件 0x55AA11EE 占位仍在——**分支处于内部不一致状态：从尖端构建的 .srec 首地址将是 0x08000000，与 BL（只写 0x08003000 起）不兼容，且会被本方案 SrecFile 首地址校验直接拒绝**。
- **联调硬前置（外部依赖，不在上位机实施范围内）**：固件侧需修复该合并（保留 FLASH 0x08003000/112K + RAM 96K）；修复前联调只能使用 **c5fcd2e** 构建的 .srec。上位机各步骤的开发、编译、无设备冒烟不受此阻塞影响，可先行实施。

### 3.4 五项适配点：固件依据 vs 上位机现状

| # | 固件新行为（依据） | 上位机现状（偏差） |
|---|---|---|
| B1 SOC | 默认 60/61/60（`fcu_protocol.c:118-124`）；写入校验新增 `soc_start_max >= soc_stop_high` 拒绝（`fcu_protocol.c:1838-1843` 返回 0x02）；soc_start_max 语义变"SOC<此值被动自启"；soc_stop_min 变保留字段（依据 `fcu_protocol.c:1226` 注释明写"保留字段"+ `fcu_control.c` 全文无 soc_stop_min 消费点，仅 start_max/stop_high 被使用；注意 `fcu_protocol.h:264` 注释仍写旧语义"关机门控"，系固件侧文档不一致，以 .c 为准） | 默认 90/85/60（`MainWindow.xaml:852,867,876`、缓存 `MainWindow.xaml.cs:92-94`）；静态提示文案 "(>90 拒启 / >85 自动停 / <60 停机前充电)"（`MainWindow.xaml:1769` tbk_BmsSocThresholdHint）+ 动态同源文案（`MainWindow.xaml.cs:3215`，跟随缓存字段）+ StartMax ToolTip 旧语义（`MainWindow.xaml:853`）共三处；`TryReadSocParamsFromUi`（421-440）**无交叉校验**，违规值发到固件才被拒 |
| B2 压力 | 默认 high_min=100 / low_min=35 / low_max=65 kPa（`fcu_protocol.c:65-75`），旧出厂组合 Flash 自动迁移 | UI 默认 300/30/80（`ViewModels/MainViewModel.cs:270-277`），ToolTip "固件默认 300/30/80 KPA"（`MainWindow.xaml:492,541,560`） |
| B3 锁定系数 | `lock_coefficient` 变保留字段，最大功率锁定不再乘系数（`fcu_protocol.h:221` 注释、README 更新日志 3b8c702） | `tbx_LoadLockFactor`（`MainWindow.xaml:1361`，文案"正常运行系数"，默认 0.900）仍可编辑并参与组包校验（`MainWindow.xaml.cs:1645-1697`） |
| B4 时序表 | 默认 14 条（`fcu_protocol.c:126-138`）；其中第 6/7 条为 11.0A/**0.75s**、15.0A/**0.75s**（`{110,75},{150,75}`）；旧第 15 条（100.0A/0.50s）已删除；扫描后由控制器计算工作电流（协议文档 §5.1） | UI 默认前 14 条中**第 6/7 行时间为 0.70 与固件 0.75 不一致**（`MainWindow.xaml:1304,1307`）；第 15 行仍默认 50.0/0.50（`MainWindow.xaml:1330-1331` tbx_Seq15_*）；组包硬编码 `data[2]=15`（`MainWindow.xaml.cs:1548-1552, 4343-4347`）；**回读回填 `UpdateLoadSequenceParams`（3417-3443）只填不清空**——回读 14 条设备后第 15 行残留旧值，再点写入会发出陈旧的第 15 条（固件接受 count≤64，静默成功） |
| B5 拉载手动 | 状态门收紧为仅 RUNNING（STARTING 拒绝 0x03）；八条命令全部返回真实错误码（`fcu_protocol.c:981-1030`） | 拉载指令响应落入通用 `HandleSetParamResponse`（`MainWindow.xaml.cs:2567-2583`），**仅底部状态栏一行文本**，会被后续状态覆盖；对比风扇 0x8F 有专门 Growl 错误弹窗（2661-2702） |

已核查无需适配的固件变化（留痕，避免后续重复排查）：PT100→PT1000 文档勘误（上位机 `MainWindow.xaml:614,1608` 已标 PT1000）、风扇启动前 4.5s 全速→低速行为（固件内部行为，上位机无对应文案）、0x0503 拉载启动参数说明细化（上位机无该语义文案）、ADC 100ms 相干采样/控制逻辑重写/Flash 参数迁移 v6（均为固件内部行为，帧格式未变）。

错误码映射核对结论：固件 `fcu_protocol.h:131-141` 与上位机 `ProtocolConstants.GetErrorCodeDescription`（449-465）**一致**（0x02=参数无效、0x03=设备忙、0x05=数据长度错误），无需修改。

## 4. 技术方案

### 4.1 XCP 主机协议实现（新增 `Protocol/OpenBltXcpClient.cs`）

C# 原生实现，不引入 LibOpenBLT：LibOpenBLT/BootCommander 为 GPLv3，与上位机（商业 OEM 分发）链接或捆绑有许可证传染风险；而本 BL 配置下协议面极小（无 seed/key、无校验字节、无块传输模式），原生实现约 300-400 行即可完全覆盖，且与既有 `BlProtocolHelper`/`BlUpgradeClient` 分层习惯一致。

**收发通道**：复用现有升级模式串口分流——`SetBlMode(true)` 后串口原始字节经 `FeedSerialByte` 进入升级 View；新增 XCP 帧重组器（`[len][payload]`，len∈[1,129]，带 200ms 帧间隙超时兜底）。发送后同步等待响应（默认超时 1000ms；PROGRAM_CLEAR 因整区擦除耗时，超时放宽到 10000ms）。

**命令集**（Intel 字节序，全部有 BL 源码依据）：

| 命令 | 请求 | 期望响应 | 用途 |
|---|---|---|---|
| CONNECT | `[FF 00]` | `[FF 10 00 81 81 00 01 01]`（maxCto/maxDto=0x81=129） | 联机（后门窗口内轮询） |
| SYNCH | `[FC]` | 已连接时 `[FE 00]`（CMD_SYNCH 错误）；**未连接时无任何响应**（`xcp.c:302-306`） | 串口失步恢复（仅会话中使用） |
| SET_MTA | `[F6 00 00 00 addr0 addr1 addr2 addr3]`（小端） | `[FF]` | 设置读写指针 |
| PROGRAM_START | `[D2]` | `[FF 00 00 81 00 00 00]` | 进入编程会话 |
| PROGRAM_CLEAR | `[D1 00 00 00 size0 size1 size2 size3]`（小端） | `[FF]` | 从 MTA 擦 size 字节 |
| PROGRAM | `[D0 N data…]`（**N≤127**，`xcp.c:1433-1437` 超限拒绝） | `[FF]` | 写 N 字节（MTA 自增）；**N=0 结束编程** |
| PROGRAM_MAX | `[C9 data×128]`（128B 定长，`xcp.c:1379`） | `[FF]` | 整块写 128B（MTA 自增） |
| PROGRAM_RESET | `[CF]` | `[FF]`（可能无响应，BL 直接跳 APP） | 结束并启动新固件 |
| UPLOAD | `[F5 n]`（n≤128，`xcp.c:774`） | `[FF data…]` | 读回校验（按 128B 分段；联调第 2 项必测的实现基础） |

标准编程序列（对齐 LibOpenBLT 会话逻辑 + 本 BL `xcp.c` 分支条件）：CONNECT → PROGRAM_START → SET_MTA(0x08003000) → PROGRAM_CLEAR(镜像实际长度) → 循环【整块用 PROGRAM_MAX(128B)、尾块用 PROGRAM(N≤127)，每次 SET_MTA 后连发】→ PROGRAM(0)（触发 BL 写校验字）→ PROGRAM_RESET。注：PROGRAM_CLEAR 无需按页/扇区取整——BL 按覆盖到的扇区整擦（`flash.c:352-367`，扇区粒度 4K），主机传镜像实际长度即可。

### 4.2 S-record 解析（新增 `Models/SrecFile.cs`）

解析 S1/S2/S3 数据记录为 `(address, bytes)` 片段列表；S0/S5 忽略；校验规则：
- 所有数据地址落在 [0x08003000, 0x0801F000) 内（112K；上界为固件参数页，不可触碰）；
- 总数据量 ≤ 112K（0x1C000）；
- 首记录地址 = 0x08003000（APP 向量表起点，栈顶指针）；
- 镜像偏移 0x1D8 处 4 字节**必须等于 0x55AA11EE**（占位字强校验——天然拦截错误分支/错误基址的构建产物，见 §3.3 阻塞说明）；
- 文件内地址无重叠。
解析产物在内存中合并为连续镜像（间隙用 0xFF 填充并记录警告），记录 `BaseAddress/Length`。

### 4.3 升级流程状态机（重写 `FirmwareUpgradeView` 升级链路）

```
[选文件] SrecFile 校验通过（含 0x1D8 占位字强校验）
   ↓
[准备·仍在 AA55 模式 9600] 发 AA55 0x07 探活：若在线且状态非 OFF/FAULT → 提示先关机
   ↓                             （注：SetBlMode(true) 后 SendAppFrame 直接 return false，
    ↓                              MainWindow.xaml.cs:4557-4561，故一切 AA55 收发必须在此之前）
[进入 BL] 方案一：仍在 9600/AA55 模式发送"加载指令"（XX 99 88 77+CRC，XX=设备地址，
        固件适配中 → 见待确认①），1s 无应答即转方案二
        方案二（回退，必然可用）：提示用户"请断电重启 FCU"
   ↓
[切升级通道] SetBlMode(true) → ReopenSerial(57600)
   ↓
[联机] 以 50ms 发送周期连发 CONNECT（单次响应等待 300ms），复位后 500ms 后门窗口内
        必有响应（断电重启场景总超时 30s）；CONNECT 响应校验（res=0x10 PGM、maxCto=129）
        → PROGRAM_START
   ↓
[擦除] SET_MTA(0x08003000) → PROGRAM_CLEAR(镜像实际长度，BL 按覆盖扇区整擦)
   ↓
[写入] 整块 PROGRAM_MAX(128B 定长)、尾块 PROGRAM(N≤127)，每块前 SET_MTA 定位；
        进度按字节反馈；单块失败重试 3 次；用户取消 → 跳失败归一化
   ↓
[固化] PROGRAM(0)（BL 计算并写入 0x1D8 校验字）
   ↓
[重启] PROGRAM_RESET → 静默 2s → ReopenSerial(9600) → AA55 0x01 探活（≤10s）
   ↓
[归一化] 成功：Growl 成功提示；失败/中断/取消：静默 6s → 切回 9600 → 探活 → NormalizeExit
```

RS485 半双工注意：BL 发送完等待 TC 标志后立即释放总线（`rs232.c:189-212`，轮询非中断），主机侧发送后立即转接收即可；无需额外方向延时。BL 侧帧间隙超时 200ms（`rs232.c:49`），与主机侧帧重组器超时互为镜像，联调排错时可对照。另：XCP CONNECT 无设备地址过滤，总线上多台 FCU 会同时应答冲突——**升级时总线上只允许连接一台 FCU**（升级页文案注明，§8 风险同步登记）。

### 4.4 加载指令帧格式（固件适配中）

用户定义：`XX 99 88 77 + CRC`，XX=设备地址。实施时在 `ProtocolConstants` 新增 `BuildLoadBootloaderCommand(byte deviceAddress)`，帧字节布局做成**单一常量+一处组包函数**，待固件适配落地后只改这一处（见待确认①）。回退断电路径不依赖该指令，保证固件适配完成前后升级功能均可用。

### 4.5 五项适配技术要点

- B1：`TryReadSocParamsFromUi` 增加 `startMax < stopHigh` 校验（错误文案说明固件新规则）；XAML 默认值 90/85→60/61；提示文案改为"(SOC<60 被动自启/>61 自动停)"；soc_stop_min 框加 ToolTip"固件当前版本中为保留字段"。
- B2：`MainViewModel.cs:270-277` 三个默认值改 100/35/65；三处 ToolTip 同步。
- B3：`tbx_LoadLockFactor` 置灰 + ToolTip"固件 V1.2 起最大功率锁定不再使用系数，此字段仅存储回读"；组包时从隐藏回填值回传（复用阀门页 stop_duration 死参数处理模式：控件保留、值透传）。
- B4：三处改动：①`tbx_Seq6_Time/tbx_Seq7_Time` 默认 0.70→**0.75**（对齐固件 `{110,75},{150,75}`）；②`tbx_Seq15_Current/tbx_Seq15_Time` 默认 Text 清空 + ToolTip"固件默认时序为 14 条，第 15 条留空则不发送"；③组包逻辑：第 15 行为空时 count=14、只组前 14 条，非空时沿用 15 条校验发送（`TryParseLoadSequences` 增加对尾随空行的跳过，**中间空行仍拒绝**）。回读回填 `UpdateLoadSequenceParams` 增加"超出 count 的行清空"（修复回读 14 条设备后第 15 行残留旧值的场景）。参数方案页 `tbx_RecipeSeq15_*` 与 `btn_ApplyRecipeToDevice_Click` 同步。
- B5：0x04 类设置固件统一回 0x82 响应（`fcu_protocol.c:1036-1048`），且 0x82 同时承载 0x0201/0x0501/0x0502/0x0503/0x0504 的结果——为 0x0504 增加专用处理：`SendLoadControlCommand` 暂存最后指令名（复用风扇页 pending+超时定时器模式，`MainWindow.xaml.cs:2661-2702`），0x0504 在途期间互斥其他 0x04 类发送；有暂存指令时 0x82 路由到新处理器（成功 Growl 含指令名；失败 Growl.Error 含指令名+错误码中文，如 0x03→"电堆需处于运行状态"；超时未响应 Growl 超时提示），无暂存时回落现有 `HandleSetParamResponse`。

### 4.6 模块关系

```
FirmwareUpgradeView (UI/状态机/进度)  ──复用──  BlHostBridgeImpl(ReopenSerial/SetBlMode/NormalizeExit)
        │                                            │
        ├── Models/SrecFile.cs (新)                   ├── SerialPortManager (唯一串口)
        └── Protocol/OpenBltXcpClient.cs (新)         └── MainWindow 串口分流 (2390-2399)
                 │
                 └── 帧收发基于 FeedSerialByte 字节流（与旧 BlUpgradeClient 同通道）
旧 BlProtocolHelper/BlUpgFile/BlUpgradeClient：不再被 UI 引用，保留在仓库
```

## 5. 影响范围

- **UI**：升级页（重写流程控件状态与提示，移除禁用横幅）、SOC 区（默认值/文案）、压力区（默认值/ToolTip）、拉载控制区（锁定系数置灰）、时序表（第 15 行默认值）、拉载手动按钮（新增 Growl 反馈）。其余页面不动。
- **业务逻辑**：升级状态机整体替换；5 处参数读写逻辑局部修改；不影响其他 AA55 读写链路。
- **数据**：无数据库；升级文件从 .upg 换为 .srec/.s19/.mot。
- **通讯**：新增 XCP over RS485（57600）实现；AA55（9600）协议层零改动；BMS Modbus 轮询经 SetBlMode 挂起/恢复机制不受影响（复用现有桥接）。
- **配置**：无配置文件变更；`BL_BAUD` 常量 9600→57600。
- **接口**：对外无新增接口；`FirmwareUpgradeView.FeedSerialByte` 复用。
- **数据库**：不涉及。
- **PLC/设备**：FCU 固件（codex 分支 + OpenBLT BL）为联调对象；旧 main（自研 BL）设备不再支持在线升级（用户已决策）。
- **第三方依赖**：**不新增**。明确不引入 LibOpenBLT/BootCommander（GPLv3 传染风险，见 4.1）。
- **线程/异步**：升级流程沿用 `Task.Run + CancellationToken + Dispatcher.BeginInvoke` 既有模式；XCP 收发在升级任务线程内同步等待。
- **性能**：112K 镜像 @57600bps ≈ 传输+写入约 30-60s（PROGRAM_CLEAR 擦除约 1-3s）；进度条按字节百分比，无 UI 阻塞。
- **兼容性**：升级功能要求设备已烧 OpenBLT BL + codex 分支 APP；对纯 beforepined 设备（无 BL），升级页开始时探测失败并明确提示"未检测到 Bootloader，需先 SWD 烧录引导"。AA55 功能对 beforepined@51bf7e2 及更早版本均兼容（适配项 B1-B5 为默认值/提示级变化，不破坏旧固件通信）。

## 6. 项目实施步骤

> 每步完成后：编译须 0 警告 0 错误 → 独立 Agent 审查（含嵌入式源码交叉验证）→ 通过后本地 git 提交，再进入下一步。

### 步骤 1：S-record 解析模型

**目标**：新增 `.srec/.s19/.mot` 固件文件解析与校验能力。

**修改位置**：上位机 → Models → 新建 `SrecFile.cs`（静态类：`TryParse(string path, out SrecFile file, out string error)`）。

**修改内容**：新增 S1/S2/S3 记录解析（每行校验行内 checksum 字节）；忽略 S0/S5；地址/长度/重叠/首地址/0x1D8 覆盖校验（规则见 4.2）；输出 `BaseAddress`（恒 0x08003000）、`Image`（连续字节数组，间隙 0xFF）、`Length`。

**实现逻辑**：逐行 `S` 记录解析 → 按 address 排序合并 → 校验链 → 生成镜像。纯内存操作，无 IO 副作用（读文件除外）。

**依赖**：无前置步骤。

**风险**：S-record 变体（SRECORD 工具 vs arm-none-eabi-objcopy 输出差异：行尾 CRLF、地址升序不保证）→ 解析按"地址排序后合并"处理乱序。

**验证方式**：用固件仓库 codex 分支构建产物实测（联调阶段）；本步先用手工构造的边界用例自测（空数据、地址越界 0x08002000/0x0801F000+、缺 0x1D8 覆盖、乱序行、坏 checksum）。

**完成标准**：边界用例全部按预期通过/拒绝；编译 0 警告 0 错误；审查通过并提交。

### 步骤 2：XCP 主机协议客户端

**目标**：实现与 Boot-For-FCU 逐字节兼容的 XCP over RS485 主机端。

**修改位置**：上位机 → Protocol → 新建 `OpenBltXcpClient.cs`（帧收发 + 命令方法 + 错误码翻译）；`ProtocolConstants.cs` 新增 XCP 常量段（命令码/错误码/超时/BL 波特率 57600；**APP/AA55 侧 9600 复用既有 `DEFAULT_BAUD_RATE`（`ProtocolConstants.cs:336`），不另新增**）。

**修改内容**：
- 帧重组器：输入字节流 → `[len][payload]` 帧（len∈[1,129]，200ms 间隙超时丢弃半帧）；**提供 Reset() 方法并在 ReopenSerial 波特率切换后调用**（清空重组缓冲，对齐旧 `BlUpgradeClient.Reset()` 先例 `Services/BlUpgradeClient.cs:43-54`）；
- 命令方法：`Connect()/Synch()/SetMta(uint)/ProgramStart()/ProgramClear(uint)/Program(byte[])/ProgramMax(byte[])/ProgramReset()/Upload(byte n)` 共 9 个（UPLOAD 为联调第 2 项必测的实现基础，`xcp.c:768-824`，n≤128），各自校验响应首字节 0xFF/0xFE 与错误码；
- **线程安全约束（验收点）**：`FeedSerialByte` 由串口 DataReceived 线程池线程调用（`MainWindow.xaml.cs:2390-2398`），帧重组缓冲与升级任务线程的同步等待之间必须加锁/线程安全队列——对齐旧 `BlUpgradeClient` 的 `_lock`+FeedByte 模式（`Services/BlUpgradeClient.cs:23,59-62`）；
- 超时：默认 1000ms；`ProgramClear` 10000ms（BL 整区擦除耗时长）；CONNECT 轮询：50ms 发送周期、单次响应等待 300ms；
- 每个方法的字节布局以 3.2 节表格为准（BL 源码行号在审查时逐条核对）。

**实现逻辑**：同步阻塞式请求-响应（运行在升级后台任务线程）；响应等待期间持续喂看门狗式地刷新超时基准。

**依赖**：无（与步骤 1 可并行）。

**风险**：与 BL 实现细节偏差（字节序、N 上限、PROGRAM(0) 语义）→ 审查时逐命令对照 `xcp.c` 源码行号验证；联调阶段用逻辑分析仪/串口日志核对。

**验证方式**：代码审查对照 BL 源码逐命令交叉验证（用户流程规则 2）；单步联调时逐命令打日志比对。

**完成标准**：9 个命令方法的请求/响应字节布局均有 BL 源码行号注释；审查通过并提交。

### 步骤 3a：升级页 UI 切换与旧路径下线

**目标**：升级页文件选择改 S-record、移除禁用横幅与旧 .upg 入口（此步不含 XCP 流程，可独立审查提交）。

**修改位置**：`Views/FirmwareUpgradeView.xaml`（横幅 tbk_UpgradeDisabledNotice 24-26 改为"本升级面向 OpenBLT 引导（需 SWD 预烧 Boot-For-FCU）；升级时总线仅连接一台 FCU"说明、按钮文案 28 行"选择 .upg 升级包"改 S-record、步骤指示文案改为 连机→擦除→写入→固化→重启、23 行配套注释清理）、`FirmwareUpgradeView.xaml.cs`（删 `UpgradeDisabledForCurrentFirmware` 及 49-56 配套注释块：57/81-85/257-264；文件过滤器 235 改 `*.srec;*.s19;*.mot`；`BL_BAUD` 60-61 → 57600，**同步更新 59 行旧注释**——旧注释"板级 RS485 约束仅支持 9600（固件 bl_uart.c BRR=26667）"是旧自研 BL 时代依据，新依据为 Boot-For-FCU `blt_conf.h:98`@2a3904b；文件校验改调 `SrecFile.TryParse`）。

**修改内容**：上述 XAML/cs 修改；旧 `BlUpgFile/BlUpgradeClient` 引用从本文件清除（类保留在仓库）。

**实现逻辑**：本步结束时"开始升级"按钮可见可用但流程函数尚未接线（下一步完成），无设备冒烟可验证：选非法/合法 srec 文件的接受与拒绝路径。

**依赖**：步骤 1。

**风险**：中间状态按钮点击无完整流程——`BtnStart_Click` 本步先弹"升级流程即将就绪"占位提示，下一步替换。

**验证方式**：编译 0 警告 0 错误；构造 5 类边界 srec 文件（§4.2 规则）验证接受/拒绝文案；无设备点击开始出现占位提示不崩溃。

**完成标准**：文件校验路径全部按预期；审查通过并提交。

### 步骤 3b：XCP 升级状态机（RunUpgradeAsync 重写）

**目标**：按 §4.3 状态机实现完整升级流程。

**修改位置**：`FirmwareUpgradeView.xaml.cs`：重写 `RunUpgradeAsync`（317-655）与 `ExitNormalizationAsync`（660-701）骨架内的调用序列；**接线点**：`FeedSerialByte`（106，现直连 `_client.FeedByte`）改为喂入 `OpenBltXcpClient` 帧重组器、`Initialize`（91，现构造 `BlUpgradeClient`）改为构造 `OpenBltXcpClient`；`MainWindow.xaml.cs` `BlHostBridgeImpl` 不改（直接复用）。

**修改内容**：
- 新流程函数：`EnterBootloaderAsync()`（9600/AA55 模式下先发加载指令→1s 无应答弹断电提示；随后 SetBlMode(true)+ReopenSerial(57600)+CONNECT 轮询 30s）、`ProgramAsync(SrecFile)`（PROGRAM_START→SET_MTA→PROGRAM_CLEAR→PROGRAM_MAX/PROGRAM 分块→PROGRAM(0)）、`FinishAndVerifyAsync()`（PROGRAM_RESET→静默 2s→ReopenSerial(9600)→0x01 探活 ≤10s）；
- **波特率常量单一来源**：本步删除 View 内 `BL_BAUD`/`APP_BAUD` 常量，统一使用步骤 2 在 `ProtocolConstants` 新增的常量（后续 BL 波特率变化只改一处，见待确认③）；
- 失败/取消归一化：任何阶段失败或用户取消走"静默 6s→切 9600→探活→NormalizeExit"（沿用 `ExitNormalizationAsync` 骨架）；
- AA55 探活/加载指令/状态检查全部在 SetBlMode(true) 之前完成（`SendAppFrame` 在升级模式下直接 return false，`MainWindow.xaml.cs:4557-4561`）。

**实现逻辑**：见 4.3 状态机。加载指令在固件适配完成前恒走回退路径，代码路径已就位。

**依赖**：步骤 1、2、3a。

**风险**：①后门 500ms 窗口错过（50ms 发送周期下窗口内可发数百帧，风险低；30s 总超时兜底）②PROGRAM_RESET 后 BL 直接跳 APP 可能不回最后 [FF] 帧 → 该命令按"发送即成功，靠后续探活判定"处理③擦除后中断：BL 区与参数页均不可擦，重启后 BL 驻留可重刷，无变砖风险。

**验证方式**：编译 0 警告 0 错误；无设备冒烟全链路（选文件→校验→AA55 探活无响应→断电提示→30s 超时→归一化退出，串口回 9600、AA55/BMS 恢复）；真机联调（第 7 章）。

**完成标准**：冒烟路径行为符合预期；审查通过并提交。

### 步骤 4：SOC 参数适配（B1）

**目标**：SOC 门控参数 UI 与固件新语义一致。

**修改位置**：`MainWindow.xaml`（852/867/876 行默认值；**1769 行静态提示 tbk_BmsSocThresholdHint**；**853 行 StartMax ToolTip 旧语义"SOC大于该值时拒绝开机"改含被动自启**；876 行 StopMin 加"保留字段"ToolTip）、`MainWindow.xaml.cs`（92-94 缓存默认值、421-440 `TryReadSocParamsFromUi` 加交叉校验、**3215 动态同源文案**）。

**修改内容**：默认 90/85→60/61；新增校验 `startMax >= stopHigh` 时报错（文案引用固件规则：被动自启阈值必须小于运行停机阈值）；**静态/动态/ToolTip 三处文案同步更新**。

**实现逻辑**：仅默认值与本地校验变化；AA55 组包/解析（`BuildSocParamsData`/`UpdateSocParams`）不动——帧格式未变。

**依赖**：无。

**风险**：现场旧固件（若仍为 90/85 语义）写入 60/61 组合会被旧固件接受（旧无交叉校验），但语义按新固件理解——提示文案已说明按新固件对齐。

**验证方式**：输入 60/61 通过、61/60 拒绝并显示新文案、**60/60 等值拒绝**（固件条件为 `>=` 拒绝）；回读回填不受影响。

**完成标准**：边界用例（60/61、61/60、1/100、100/1）行为正确；审查通过并提交。

### 步骤 5：压力参数默认值适配（B2）

**目标**：UI 默认值与固件 V1.2 默认一致。

**修改位置**：`ViewModels/MainViewModel.cs:270-277`（`_highPressureMin="300"→"100"`、`_lowPressureMin="30"→"35"`、`_lowPressureMax="80"→"65"`）；`MainWindow.xaml:492,541,560` 三处 ToolTip。

**修改内容**：三个默认值与三处 ToolTip 文案。

**实现逻辑**：仅展示默认值；设备实际值仍以 0x0101 回读为准（回读链路不动）。

**依赖**：无。

**风险**：低。旧固件设备回读 300/30/80 仍正常显示（回读覆盖默认值）。

**验证方式**：打开页面默认显示 100/35/65；连接设备回读后显示设备实际值（真机项，联调阶段）。

**完成标准**：默认值/ToolTip 与 `fcu_protocol.c:65-75` 一致；审查通过并提交。

### 步骤 6：锁定系数置灰（B3）

**目标**：0x0502 锁定系数按固件"保留字段"语义置灰标注。

**修改位置**：`MainWindow.xaml:1361`（`tbx_LoadLockFactor` 加 `IsEnabled="False"` + ToolTip）；`MainWindow.xaml.cs:1645-1697`（组包时锁死参数改从隐藏缓存回填——读取上次回读值或默认 900 透传，控件值不再参与）；**`MainWindow.xaml.cs:3448-3454`（`UpdateLoadControlParams` 回读处同步更新 `_lastLockFactor0P001` 缓存，3453 行 `tbx_LoadLockFactor.Text` 赋值处）**。

**修改内容**：控件置灰 + 死参数透传（复用阀门页 stop_duration 处理模式）。

**实现逻辑**：新增 `_lastLockFactor0P001` 缓存（回读时更新，初值 900=固件默认）；组包用缓存值。

**依赖**：无。

**风险**：低。回读仍显示固件存储值（灰色控件可显示不可编辑）。

**验证方式**：置灰生效（无真机即可验证）；写入 0x0502 帧中锁定系数字节=透传值、回读后灰框显示设备值（真机项，联调阶段）。

**完成标准**：帧字节核对正确；审查通过并提交。

### 步骤 7：拉载时序表默认值与回读适配（B4）

**目标**：UI 默认时序与固件 14 条默认完全一致，并修复回读残留场景。

**修改位置**：`MainWindow.xaml:1304、1307`（`tbx_Seq6_Time/tbx_Seq7_Time` 默认 0.70→0.75）、`MainWindow.xaml:1330-1331`（`tbx_Seq15_Current/tbx_Seq15_Time` 默认 Text 清空 + ToolTip）；`MainWindow.xaml.cs:1536-1594`（`btn_SetLoadSequenceParams_Click` 组包：第 15 行空→count=14）、`2007-2037`（`TryParseLoadSequences` 支持尾空行、拒绝中间空行）、**`3417-3443`（`UpdateLoadSequenceParams` 回读回填：超出 count 的行清空）**；方案页 `MainWindow.xaml.cs:4331-4390` 同步。

**修改内容**：三组改动——第 6/7 行时间默认值、第 15 行默认清空+组包数量动态（14 或 15）、回读清空多余行。方案页 Recipe 模型第 15 条默认 0 视为空。

**实现逻辑**：`TryParseLoadSequences` 返回实际有效条数（从后往前跳过空行，仅允许尾部连续空行，中间空行报错）；组包 `data[2]=有效数`；回填时 `for i in [count, 15)` 清空行。

**依赖**：无。

**风险**：中间空行（第 8 行空、第 9 行有值）必须拒绝——校验规则明确"仅允许尾部连续空行"。

**验证方式**：①默认直接写入→帧 count=14 且第 6/7 条时间为 75（0.75s）；②填满 15 行→count=15；③中间空行→报错；④**先写入 15 条→回读 14 条设备→第 15 行应为空**，再点写入→帧 count=14（不重发陈旧条目）。

**完成标准**：四种输入/回读路径行为正确；审查通过并提交。

### 步骤 8：拉载手动命令 Growl 反馈（B5）

**目标**：拉载手动八命令的执行结果以弹窗级反馈呈现，错误码语义准确。

**修改位置**：`MainWindow.xaml.cs`：0x82 响应分发处（2447-2450，固件对 0x04 类请求统一回 0x82，`fcu_protocol.c:1036-1048`）为 0x0504 子命令增加专用处理器（新增 `HandleLoadManualResponse` + 指令暂存 `_lastLoadCmdDescription` + 超时定时器，复用风扇页 pending 模式 2661-2702）；`SendLoadControlCommand`（2049-2076）记录指令名并在 0x0504 在途期间互斥其他 0x04 类发送。

**修改内容**：新增处理器 + 暂存 + 超时提示；有暂存 0x0504 指令时 0x82 路由到新处理器，无暂存时回落现有 `HandleSetParamResponse`（0x82 仍承载 0x0201/0x0501/0x0502/0x0503 结果）。成功 Growl（含指令名），失败 Growl.Error（指令名+错误码中文+状态提示，如 0x03→"电堆需处于运行状态"），超时 Growl 超时提示。

**实现逻辑**：AA55 响应路由局部变化；`HandleSetParamResponse` 仍处理其余 0x04 子类型。

**依赖**：无。

**风险**：0x82 响应不含子命令回显——靠暂存关联；互斥 + 回落规则已消除误路由窗口（无暂存时不会误吞其他 0x04 结果）。

**验证方式**：模拟错误码路径（设备 STARTING 时发"启动拉载"→ Growl 显示"设备忙"，真机项，联调阶段）；成功路径 Growl 正常；超时路径（发指令后拔线）Growl 超时提示。

**完成标准**：成功/失败/超时/未知码四路径呈现正确；审查通过并提交。

## 7. 测试与验证方案

- **静态**：每步编译 0 警告 0 错误（项目既有门槛）；XCP 命令布局逐条对照 BL 源码审查。
- **单元级自测**：SrecFile 边界用例（步骤 1 验证方式）；SOC 交叉校验边界（步骤 4）；时序表三路径（步骤 7）。
- **无设备冒烟**：升级页全流程在无设备下走"文件校验（含 0x1D8 占位字拦截）→AA55 探活无响应→断电提示→30s 超时→归一化退出"，UI 不卡死、串口恢复 9600、AA55/BMS 恢复轮询；另测"用户中途取消→失败归一化"分支与"升级中串口 IO 异常/USB 拔出→异常终止→归一化不卡死"分支（`ExitNormalizationAsync` 骨架兜底）。
- **真机联调**（**硬前置：固件侧修复 codex 分支 abc0115 的链接基址回退，或使用 c5fcd2e 构建产物**；需烧录 Boot-For-FCU BL + 对应 APP）：
  1. 正常升级：旧 APP → 新 APP（S-record），验证擦/写/固化/重启/探活全绿；
  2. UPLOAD 读回全镜像分段（128B/次）比对——**联调至少执行一次**，可拦截主机侧静默写入错位；**比对不一致则立即中止并走失败归一化，禁止 PROGRAM_RESET**；
  3. 断电重启进入 BL 路径（固件加载指令未就绪时的主路径）；
  4. 加载指令路径（固件适配完成后回归一次，同时确认该指令允许的设备状态，见待确认①）；
  5. 升级中途拔电（写入 50% 时）→ 重上电 → BL 驻留 → 重新升级成功（验证不变砖，且设备参数页 0x0801F000 保留、参数不丢）；
  6. 错误路径：选错文件（地址越界/0x1D8 非 0x55AA11EE 的 srec）→ 主机侧拒绝、绝不发 PROGRAM_CLEAR；BL 不存在（纯 beforepined 设备）→ 明确提示需 SWD 烧 BL；**设备 RUNNING 状态点开始升级 → 被"非 OFF/FAULT 先关机"前置拦截、不进入 BL 模式**；
  7. 逻辑分析仪/串口日志观测 PROGRAM_RESET 的 [FF] 是否发出（待确认④取证项；可选加一条会话中 SYNCH 失步恢复用例）。
- **回归**：B1-B5 各自页面读写正常；非升级页面 AA55 通信无变化；BMS 轮询在升级前后正常挂起/恢复。
- **兼容性**：beforepined@51bf7e2 之前版本的旧固件设备上，B1-B5 改动不破坏通信（帧格式未变，仅默认值/校验/提示变化）。

## 8. 风险与注意事项

- **高风险**：升级写入阶段中断电——OpenBLT 下 BL 区与参数页（0x0801F000）均不可擦，最坏情况 APP 无效、设备驻留 BL、参数保留，可重新升级，**无变砖风险**（优于旧自研 BL）；但升级过程中严禁断开串口程序强杀进程（主机侧串口异常断开不影响设备侧 BL 继续等待）。
- **外部阻塞依赖**：codex 分支尖端 abc0115 链接基址回退（见 §3.3）——真机联调被此阻塞，上位机开发不受影响；SrecFile 的 0x1D8 占位字/首地址强校验是主机侧防线，错误产物在发送前即被拦截。
- **RS485 多站风险**：XCP CONNECT 无设备地址过滤（不同于 AA55 的 ADDR 字节），总线上多台 FCU 同时应答会冲突——升级时总线仅连接一台 FCU，升级页文案注明（步骤 3a）。
- **c5fcd2e 产物 RAM 风险**：回退前提交 c5fcd2e 的 .ld 中 RAM LENGTH=128K 同为错误值（实际主 SRAM 96K），使用其构建产物联调前须检查 map 文件 RAM 用量未越过 0x20018000（96K 边界），否则联调死机会被误判为上位机 XCP 实现问题。
- **兼容性**：旧 main（自研 BL）设备不再支持在线升级（用户已决策下线 .upg 路径）；纯 beforepined 无 BL 设备升级页给出明确引导文案。
- **数据风险**：S-record 防错刷校验（地址窗/首地址/0x1D8 占位字）是唯一防线——不可省略；错误文件在主机侧拦截，绝不发 PROGRAM_CLEAR。
- **性能**：PROGRAM_CLEAR 擦 112K 全区约数秒（BL 内逐扇区擦+喂狗），主机超时取 10s，联调时按实测调整。
- **回滚**：上位机 git 本地提交逐步可回滚；设备侧回滚 = 用另一个**codex 分支持续产物**（0x08003000 链接）再刷一次；**回退到 beforepined 旧 APP（链接于 0x08000000）无对应 S-record 产物，只能 SWD 烧录**——此边界须在升级页提示文案中告知用户。
- **第三方依赖**：不新增（GPL 规避见 4.1）。
- **待确认事项**：
  - ① **加载指令帧格式与适用状态**：用户定义为 `XX 99 88 77 + CRC`（XX=设备地址），固件侧适配中。完整字节布局待固件落地后对齐：是否含 AA 55 帧头、FUNC/LEN 字段取值、CRC 算法（推测 CRC16-CCITT 同 AA55）与覆盖范围、**该指令允许的设备状态**（若任意状态可进 BL，则 §4.3 的"非 OFF/FAULT 先关机"前置检查需放宽）、**加载指令进入 BL 后的驻留时长**（BL 后门默认仅 500ms，主机 ReopenSerial 的 Close→Open 含至多 3 次重试每次歇 200ms（`MainWindow.xaml.cs:4527-4554`），若重开耗时超窗、BL 跳回仍有效的 APP，30s 轮询必超时——需与固件同事确认进 BL 方式（如置无效签名/延长窗口）能覆盖该延时；断电回退路径不受影响）。实施按 4.4 单点封装，届时只改一处。
  - ② codex/openblt-app-upgrade 分支尖端 abc0115 的 .ld 合并回退需固件侧修复（保留 FLASH 0x08003000/112K + RAM 96K）；修复前联调用 c5fcd2e 产物。
  - ③ BL 波特率 57600 为 2026-09-21 最新提交（2a3904b），若后续再调整，上位机 `BL_BAUD` 常量一处同步。
  - ④ PROGRAM_RESET 响应时序（BL 跳 APP 前是否稳定回出最后 [FF] 帧）需联调确认（第 7 章第 7 项取证），代码按"允许无响应"处理。

## 9. 完成标准

1. 步骤 1、2、3a、3b、4-8 全部完成，每步独立审查通过并本地 git 提交（提交号留痕）。
2. 最终全量编译 0 警告 0 错误。
3. 真机联调清单（第 7 章）1/2/3/5/6 **五项必过**（第 2 项 UPLOAD 读回比对的实现含在步骤 2 的 9 个命令方法中）；第 4 项（加载指令路径）在固件适配落地后补测；第 7 项为取证记录项，结果回填待确认④，不设通过门槛；全部联调以固件侧修复 §3.3 链接基址回退或改用 c5fcd2e 产物（含 RAM 用量检查，见 §8）为硬前置。
4. 升级页在无 BL 设备上给出明确"需 SWD 烧录引导"提示，不出现未处理异常。
5. B1-B5 验证用例（各步骤"验证方式"，含步骤 7 的回读残留用例④）全部通过。
6. 升级流程结束后串口回到 9600 且 AA55 探活、BMS 轮询恢复正常（回归项）。
7. 旧 .upg 路径在 UI 上不可达，且 `BlProtocolHelper/BlUpgFile/BlUpgradeClient` 三文件保留在仓库（diff 验证未删除）。
