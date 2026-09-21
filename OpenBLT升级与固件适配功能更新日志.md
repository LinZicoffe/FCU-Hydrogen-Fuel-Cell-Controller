# OpenBLT升级与固件适配功能更新日志

## 当前目标

按《docs/实施步骤-OpenBLT升级与固件适配.md》（3 轮评审通过）完成上位机开发：①新增基于 OpenBLT XCP over RS485（57600bps）的在线升级功能（S-record 解析、XCP 客户端、升级页重写、旧 .upg 路径 UI 下线）；②完成固件 2026-09-17 更新引出的 5 项协议适配（SOC/压力/锁定系数/时序表/拉载反馈）。

## 开发约束

1. 不修改任何嵌入式代码（450V_FCU、Boot-For-FCU 两仓库只读）
2. 每步修改须有嵌入式源码依据（实施文档已逐条给出，实现时照引）
3. 每步：修改 → 编译 0 警告 0 错误 → 独立 Agent 审查 → 通过后本地 git 提交
4. 旧 BlProtocolHelper/BlUpgFile/BlUpgradeClient 三文件保留不删，仅下线 UI 引用
5. 不做拉载时序表 15→64 行 UI 扩展；不升版本号、不打包
6. 范围外文件：用户文件《FCU参数新增与建议清单(1).md》（未跟踪，不提交不修改）

## 待办列表

| ID | 待办描述 | 预计代码位置 | 状态 |
|----|----------|--------------|------|
| A | S-record 解析模型（S1/S2/S3 解析+地址窗/首地址/0x1D8 占位字强校验） | Models/SrecFile.cs（新建，实际 246 行） | 已完成 |
| B | XCP 主机协议客户端（9 命令+帧重组器+线程安全+常量段） | Protocol/OpenBltXcpClient.cs（新建）、Protocol/ProtocolConstants.cs（新增 XCP region） | 已完成 |
| C | 升级页 UI 切换与旧路径下线（横幅/过滤器/BL_BAUD/死注释清理/占位提示） | Views/FirmwareUpgradeView.xaml(.cs) | 已完成 |
| D | XCP 升级状态机（RunUpgradeAsync 重写+接线+加载指令+断电回退+归一化） | Views/FirmwareUpgradeView.xaml.cs、Protocol/ProtocolHandler.cs（BuildLoadBootloaderCommand） | 已完成 |
| E | SOC 参数适配（默认 60/61、start<stop 交叉校验、三处文案） | MainWindow.xaml:841-898、MainWindow.xaml.cs:91-95/422-444/2738-2752/3188-3202/3220-3227、Models/ProtocolModels.cs | 已完成 |
| F | 压力参数默认值适配（100/35/65+ToolTip） | ViewModels/MainViewModel.cs:270-277、MainWindow.xaml:492/541/560 | 未开始 |
| G | 锁定系数置灰（死参数透传+回读缓存） | MainWindow.xaml:1361、MainWindow.xaml.cs:1645-1697/3448-3454 | 未开始 |
| H | 时序表默认值与回读适配（0.75s/第15行清空/count动态/回读清空） | MainWindow.xaml:1304/1307/1330-1331、MainWindow.xaml.cs:1536-1594/2007-2037/3417-3443/4331-4390 | 未开始 |
| I | 拉载手动命令 Growl 反馈（0x0504 专用处理器+在途互斥+超时） | MainWindow.xaml.cs:2049-2076/2447-2450、新增 HandleLoadManualResponse | 未开始 |

## 已知风险

1. 真机联调被固件侧阻塞：codex/openblt-app-upgrade 分支尖端 abc0115 链接基址合并回退（实施文档 §3.3），联调须用 c5fcd2e 产物或等固件修复——不影响本分支全部开发步骤
2. 加载指令帧格式待固件适配后对齐（实施文档待确认①）；固件未落地前 D 步骤恒走断电回退路径

## 其他

1. 范围外遗留：诊断用二补数预估日志（联调时按需实现）；版本号升级与安装包重打另行安排
2. SrecFile 的 S5 记录数一致性校验（审查建议的增强项，不采纳实施，理由：非本待办要求，objcopy 产物截断场景已由行 checksum+占位字校验兜底）

---

## 更新记录

### 2026年09月21日（更新5）

**更新内容**

待办 E（实施文档步骤 4/B1）：SOC 参数适配。默认值 90/85→60/61（XAML 三框+cs 缓存字段，对齐固件 fcu_protocol.c:118-124）；`TryReadSocParamsFromUi` 新增 `startMax >= stopHigh` 前置拦截（与固件 fcu_protocol.c:1838-1843 等值，含 60/60 等值拒绝），错误文案+Growl 解释滞回语义；表头/ToolTip/静态提示(xaml:1769)/动态提示(ApplyActiveSocParams)/初始默认值文案(xaml:898)/启动失败文案(BuildStartFail03Message)/TryParseRange 字段名/SocParams 模型注释 全部迁移至新语义（"被动自启阈值"+"关机充电下限(保留)"，依据 fcu_control.c 零消费点+fcu_handle_set_soc_params 注释）；回读状态文案三元组去掉无运行语义的 StopMin。
验证：编译 0 警告 0 错误；60/61 通过、61/60 与 60/60 拒绝（代码走查+审查员对照固件逐条核验不等号方向）。

**涉及位置**

- MainWindow.xaml（841/844 表头、852/867/876 默认值+ToolTip、898 初始文案、1769 静态提示）
- MainWindow.xaml.cs（91-95 缓存、422-444 交叉校验、2738-2752 启动失败文案、3189/3202 回读文案、3220-3227 动态提示）
- Models/ProtocolModels.cs（SocParams 注释）

**对应待办**

- E

**审查结果**

第 1 轮不通过（表头/字段名旧语义与 ToolTip 矛盾）→ 修复；第 2 轮新 Reviewer 复审不通过（tbk_SocValidationStatus 初始文案 90/85 残留）→ 修复；第 3 轮新 Reviewer 通过（零阻塞，SOC 区 grep 确认无旧值残留）。遗留风险记录：固件侧 fcu_protocol.c:123 注释与 :1226 保留字段注释自相矛盾（固件侧问题，不在上位机范围）；旧设备 Flash 残留 90/85 时滞回失效，需重写 60/61。

**Git Commit**

feat(E): SOC参数适配——默认60/61、start<stop交叉校验、语义文案全量迁移

### 2026年09月21日（更新4）

**更新内容**

待办 D（实施文档步骤 3b）：XCP 升级状态机完整重写。RunUpgradeAsync 按 §4.3 状态机实现：前置检查（0x07 探活，非 OFF/FAULT 拒绝；无应答视为可能 BL 常驻直接进 XCP 轮询）→ 加载指令（ProtocolHandler.BuildLoadBootloaderCommand 单点封装，裸序列 ADDR 99 88 77 + CRC16-CCITT 大端，待确认①；无应答 1s 转断电重启提示）→ SetBlMode(true)+ReopenSerial(57600)+CONNECT 轮询（50ms/300ms/30s，校验 res=0x10+maxCto=129）→ PROGRAM_START→SET_MTA(0x08003000)→PROGRAM_CLEAR→PROGRAM_MAX 整块/PROGRAM 尾块循环（失败重试 3 次+重设 MTA+进度速率）→PROGRAM(0) 固化→[VERIFY_BY_UPLOAD 开关读回校验，跳过 0x1D8-0x1DB]→PROGRAM_RESET→静默 2s→9600→0x81 探活 10s→成功 NormalizeExit(true)；失败/取消走 ExitNormalizationAsync（blModeEntered 参数：未进 BL 模式跳过 6s 静默）。
审查建议落实：存活循环复查取消标志（区分取消/超时）、catch(Exception) 补用户弹窗、Task.Run 去掉第二 ct 实参（防 _busy 卡 true）、加载指令调用点标注固件对齐义务。XAML 头注释更新、tbk_UpgradeDisabledNotice 改名 tbk_UpgradeNotice。
验证：编译 0 警告 0 错误；无设备冒烟路径经审查员代码走查闭合（选文件→探活无应答→30s 轮询超时→归一化恢复，约 50s 无卡死）；真机联调待固件侧前提（见已知风险 1）。

**涉及位置**

- FCU 氢燃料电池控/Views/FirmwareUpgradeView.xaml.cs（RunUpgradeAsync/VerifyByUploadAsync/ExitNormalizationAsync/DescribeXcp/BtnStart_Click）
- FCU 氢燃料电池控/Views/FirmwareUpgradeView.xaml（头注释、tbk_UpgradeNotice）
- FCU 氢燃料电池控/Protocol/ProtocolHandler.cs（新增 BuildLoadBootloaderCommand）

**对应待办**

- D

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞，独立复现编译 0/0；逐项对照 Boot-For-FCU xcp.c/flash.c 源码核验命令序列与时序）。建议 1-5 已落实，建议 6 小项（失败步标⑤/RequestAppFrame 不校验地址）不采纳（与旧代码行为一致、单台 FCU 约定已由横幅与风险记录覆盖）。

**Git Commit**

feat(D): XCP升级状态机——加载指令/断电回退/连机/擦写固化/重启验证/归一化

### 2026年09月21日（更新3）

**更新内容**

待办 C（实施文档步骤 3a）：升级页 UI 切换与旧路径下线。横幅改为 OpenBLT 引导说明（SWD 预烧 Boot-For-FCU + 升级期间仅连接一台 FCU——XCP 无地址过滤多站同答冲突）；文件选择改 S-record（*.srec/*.s19/*.mot，SrecFile.TryLoad 校验+间隙警告展示）；删除 UpgradeDisabledForCurrentFirmware 及配套注释/构造禁用/BtnStart 防御；删除旧 S1-S9 流程（RunUpgradeAsync/ExitNormalizationAsync 整体，待办 D 重写）；BL_BAUD/APP_BAUD 改引用 ProtocolConstants 单一来源并清理旧"仅支持 9600"注释；Initialize 构造 OpenBltXcpClient、FeedSerialByte 改喂 XCP 帧重组器；本文件对 BlUpgFile/BlUpgradeClient/BlProtocolHelper 零引用（三文件保留在仓库）；五步指示文案更新；BtnStart 占位提示（待办 D 接入状态机）；解析失败回置"未选择"（审查建议落实）。
验证：编译 0 警告 0 错误；MainWindow 接线（Initialize 193/分流 2390-2398/IsBusy 2914）接口匹配未动。

**涉及位置**

- FCU 氢燃料电池控/Views/FirmwareUpgradeView.xaml（横幅/按钮/步骤文案/注释）
- FCU 氢燃料电池控/Views/FirmwareUpgradeView.xaml.cs（重写，709→约 290 行）

**对应待办**

- C

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞，独立复现编译 0/0）。建议 2 已落实（失败回置路径）；建议 1（XAML 头部注释）与 3（tbk_UpgradeDisabledNotice 改名）留待办 D 顺手处理。

**Git Commit**

feat(C): 升级页切换OpenBLT/S-record链路，旧.upg路径UI下线（占位待D）

### 2026年09月21日（更新2）

**更新内容**

待办 B：新增 `OpenBltXcpClient`——XCP over RS485 主机端。帧重组器 `[len][payload]`（len∈[1,129]、整帧首字节起 200ms 超时忠实镜像 rs232.c:248/297、错误帧 [FE xx] 豁免 expectedLen 过滤）；9 个命令方法（Connect/Synch/SetMta/ProgramStart/ProgramClear/Program/ProgramMax/ProgramReset/Upload）逐条对照 Boot-For-FCU xcp.c 带行号注释；锁+Monitor.Wait 线程模型对齐 BlUpgradeClient；ProtocolConstants 新增 OpenBLT XCP 常量段（命令码/错误码/超时/BL 波特率 57600，APP 侧复用 DEFAULT_BAUD_RATE）。
审查发现并修复 2 个真 bug：①接收缓冲差一字节（129B 满帧 Upload(128) 越界，+1 分配 130B）②错误帧被 expectedLen=1 命令当迟到帧丢弃（显式错误全部误报超时）。复审另纠正 Synch 判定注释（应 !Timeout && Data.Length==2，Ok 恒 false）与帧空闲语义（改回首字节基准）。
验证：主工程编译 0 警告 0 错误；模拟 BL 应答器自测 19 用例全过（逐命令帧字节比对/小端地址长度/错误帧/迟到帧/半帧间隙/len 越界/超时/参数防御/PROGRAM_RESET 双路径/Upload(128) 满帧 129B 回归）。

**涉及位置**

- FCU 氢燃料电池控/Protocol/OpenBltXcpClient.cs（新增）
- FCU 氢燃料电池控/Protocol/ProtocolConstants.cs（新增 XCP 常量 region，+82 行，其余未动）

**对应待办**

- B

**审查结果**

第 1 轮不通过（阻塞：_rxBuf 差一字节）→ 修复 → 第 2 轮新 Reviewer 复审通过（零阻塞）。建议项全部落实：Upload 参数防御、SendData 失败注释、Synch 判定注释、帧空闲首字节基准、Upload 下界措辞。

**Git Commit**

feat(B): OpenBLT XCP客户端，9命令+帧重组器，修复缓冲越界与错误帧误报

### 2026年09月21日（更新1）

**更新内容**

待办 A：新增 `SrecFile`——OpenBLT 升级用 S-record(Motorola S1/S2/S3) 解析与校验。逐行校验 byte count 与行 checksum；五条强校验（地址窗 [0x08003000,0x0801F000)、首地址=基址、跨度 ≤112K、镜像偏移 0x1D8 处小端占位字 0x55AA11EE、排序后相邻无重叠）；产物合并为连续镜像（间隙 0xFF 填充并记入 Warnings）。自测抓出并修复地址长度 bug（S1/S2/S3 地址字节数应为类型号+1）。API 为 `TryLoad(path, out error)`（与 BlUpgFile 风格一致；实施文档示意签名 TryParse 的合理偏离，后续步骤 D 接线时按 TryLoad 调用）。
验证：主工程编译 0 警告 0 错误；临时控制台工程 13 个边界用例全过（正常分片/空数据/低于基址/越过参数页/恰好 112K/缺 0x1D8/占位字错/坏 checksum/乱序行/重叠/间隙警告+0xFF 填充/S7+空行/镜像内容抽验）。

**涉及位置**

- FCU 氢燃料电池控/Models/SrecFile.cs（新增）

**对应待办**

- A

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞）。建议落实：删除死变量 copied、新增地址环绕检测（均已修改并复验 13/13 通过）；不采纳：S5 记录数校验（增强项，记入"其他"）。

**Git Commit**

feat(A): S-record解析模型SrecFile，五条强校验+间隙0xFF合并
