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
| F | 压力参数默认值适配（100/35/65+ToolTip） | ViewModels/MainViewModel.cs:268-277、MainWindow.xaml:492/541/560 | 已完成 |
| G | 锁定系数置灰（死参数透传+回读缓存） | MainWindow.xaml:1357-1362、MainWindow.xaml.cs:97-99/1658-1700/3466-3487 | 已完成 |
| H | 时序表默认值与回读适配（0.75s/第15行清空/count动态/回读清空） | MainWindow.xaml:1304/1307/1330-1335、MainWindow.xaml.cs:1549-1611/2024-2118/3463-3510/4395-4432/4528-4566 | 已完成 |
| I | 拉载手动命令 Growl 反馈（0x0504 专用处理器+在途互斥+超时） | MainWindow.xaml.cs（SendLoadControlCommand/HandleLoadManualResponse/超时定时器/五处互斥/八处理器） | 已完成 |
| R1 | 终审建议返工（SendFrame 失败回滚/CONNECT 轮询注释口径/取消 fail 标真实步骤） | MainWindow.xaml.cs:2215-2225、ProtocolConstants.cs:456-458、FirmwareUpgradeView.xaml.cs:310-500 | 已完成 |

## 已知风险

1. 真机联调被固件侧阻塞：codex/openblt-app-upgrade 分支尖端 abc0115 链接基址合并回退（实施文档 §3.3），联调须用 c5fcd2e 产物或等固件修复——不影响本分支全部开发步骤。**当前交付口径：升级功能代码完备、硬件未验证**
2. 加载指令帧格式待固件适配后对齐（实施文档待确认①）；固件未落地前 D 步骤恒走断电回退路径。**上线前回归项：固件加载指令落地后，须回到 FirmwareUpgradeView.xaml.cs 加载指令分支改为"等待确认帧再分支"（当前无条件 1s 转断电提示），并回归实施文档 §7 联调第 4 项**
3. **联调第 2 项（UPLOAD 全镜像读回比对）启用方式：将 FirmwareUpgradeView.xaml.cs 的 VERIFY_BY_UPLOAD 置 true 后重编译（编译期开关默认 false）**
4. 旧固件兼容性提示（随版本发布说明同步）：旧固件 SOC 存量 90/85 组合会被新交叉校验拦截（需改 60/61）；未回读直接写 0x0502 时锁定系数按 900 透传；旧固件若按 63B 定长校验 0x0501 则 14 条默认写入会被拒（用户可手填第 15 行）；升级后轮询不自动恢复需手动重启

## 其他

1. 范围外遗留：诊断用二补数预估日志（联调时按需实现）；版本号升级与安装包重打另行安排
2. SrecFile 的 S5 记录数一致性校验（审查建议的增强项，不采纳实施，理由：非本待办要求，objcopy 产物截断场景已由行 checksum+占位字校验兜底）
3. 文案统一类小项（待办 F 审查建议）：ParsePressureField 字段名"高压最小值"改"高压启动门槛"；高压上限 ToolTip"建议初值 5000"改"固件默认 5000"措辞
4. 死代码清理（待办 G 审查建议）：MainWindow.xaml.cs:1614-1659 五个未绑定 XAML 的孤儿按钮 handler（btn_SetLoadLockFactor_Click 等，先于本任务存在）
5. 方案页量纲错配（待办 H 审查发现，预先存在非本次引入）：配方框显示原始值（0.1A 整数），应用时复用的 TryParseLoadSequences 按显示值×10 解析——预设配方电流>50 的行超范围报错、≤50 的行按 10 倍放大发送；需独立待办统一方案页量纲
6. 0x04 类互斥为单向（待办 I 审查建议不采纳根治）：0x0501/02/03 自身无在途跟踪，毫秒级往返窗口内抢点 0x0504 可能误归属一次 Growl（真实结果仍会显示）；根治方案（发送时间戳间隔 ≥500ms）留待后续
7. 终审未采纳实施的建议（记录备查）：①XCP Request 发送前清接收缓冲消除"迟到帧串答"（遗留模式风险，触发条件=响应延迟>1s，VERIFY_BY_UPLOAD 可对冲，联调前可加固）②VERIFY_BY_UPLOAD 改运行时复选框（联调便利项）③ExitNormalizationAsync catch 末尾兜底强关串口 ④SrecFile 13 用例与 XCP 19 用例固化为仓库内测试工程（测试资产沉淀）⑤文档勘误两处（BuildLoadBootloaderCommand 实际落位 ProtocolHandler.cs 而非 §4.4 所写 ProtocolConstants；SrecFile API 实为 TryLoad 而非文档示意 TryParse——均已在本日志登记）

---

## 更新记录

### 2026年09月21日（更新11）

**更新内容**

OpenBLT升级与固件适配功能开发完成。

**完成情况**

- 所有开发待办（A~I + R1）已完成
- 本地验证已通过：全量编译 0 警告 0 错误（Debug/Release 双配置，终审 Reviewer 独立复现）；SrecFile 13 边界用例 + XCP 模拟 BL 应答器 19 用例全过；每步独立审查（待办 E 3 轮、B/I 2 轮、其余 1 轮，全部通过）
- 最终项目级审查已通过：3 个全新 Reviewer（需求完整性/代码与架构/风险与测试）并行审查，全部零阻塞通过；终审建议中 3 处已作为 R1 返工落实（SendFrame 失败回滚、CONNECT 轮询注释口径、取消 fail 标真实步骤），其余记录于"其他"
- 真机联调为外部阻塞依赖（固件侧 codex 分支链接基址回退，见已知风险 1），联调清单见实施文档 §7
- 最终 Git 提交随本条目一并执行

**最终状态**

开发完成（停在 feature/openblt-upgrade-adapt 分支，未合并 master、未推送、未打包）

### 2026年09月21日（更新10）

**更新内容**

终审返工待办 R1（终审 Reviewer B 三条建议）：①SendLoadControlCommand 检查 SendFrame 返回值——串口写失败（含 BL 升级模式拦截）立即回滚 pending/停定时器/Growl 提示/返回 false，消除"假已发送+互斥空挂 5s"；②CONNECT 轮询注释口径修正（实际周期 ≈350ms=300ms 等待+50ms 间隔 < 500ms 后门窗口，两处注释同步）；③升级取消时 fail 标色标在真实进行步骤（activeStep 变量随四步推进，原固定标第⑤步）。附：returns 注释枚举补全（R1 审查建议）。
验证：编译 0 警告 0 错误（审查员独立复现）。

**涉及位置**

- MainWindow.xaml.cs（SendLoadControlCommand 失败回滚+returns 注释）
- Protocol/ProtocolConstants.cs（XCP_CONNECT_WAIT_MS 注释）
- Views/FirmwareUpgradeView.xaml.cs（activeStep 追踪+注释）

**对应待办**

- R1

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞）。

**Git Commit**

fix(R1): 终审返工——发送失败回滚/轮询注释口径/取消fail标真实步骤 → 692d653

### 2026年09月21日（更新9）

**更新内容**

待办 I（实施文档步骤 8/B5）：拉载手动命令弹窗级反馈。SendLoadControlCommand 记录 _pendingLoadManualCmd + 5s DispatcherTimer 超时（pending+超时模式）；0x82 分发路由 HandleLoadManualResponse——接收线程快照 pending（防 5s 边界空名 Growl），有暂存走专用处理器（成功 Growl.Success 含指令名/失败 Growl.Error 含错误码中文，0x03 追加"电堆需处于运行状态"对齐固件 RUNNING 门/超时 Growl.Warning），无暂存回落 HandleSetParamResponse；五个 0x04 发送点全部加在途互斥（防 0x82 无回显误路由）；八个按钮处理器按 SendLoadControlCommand 返回值条件化"已发送"提示；断开与 OnClosed 清 pending/停表。
验证：编译 0 警告 0 错误；成功/失败/超时三路径经审查员代码走查闭合，固件 0x82/0x03/cmd 0x10-0x17/错误码表逐项对照一致。

**涉及位置**

- MainWindow.xaml.cs：字段（_pendingLoadManualCmd/_loadManualResponseTimeoutTimer）、定时器四件套、SendLoadControlCommand（返回 bool）、GetLoadCommandDescription、HandleLoadManualResponse、0x82 分发、五处互斥、八处理器、OnSerialConnectionChanged/OnClosed 清理

**对应待办**

- I

**审查结果**

第 1 轮不通过（配方页第五发送点漏互斥）→ 修复 + 三建议落实（OnClosed 清理、快照模式、假状态提示）→ 第 2 轮新 Reviewer 通过（零阻塞，五点互斥覆盖经 grep 全量确认）。不采纳：单向互斥毫秒级窗口的根治（发送时间戳方案，记入"其他"）。

**Git Commit**

feat(I): 拉载手动命令Growl反馈——0x0504专用处理器+五点在途互斥+超时提示 → a162aa3

### 2026年09月21日（更新8）

**更新内容**

待办 H（实施文档步骤 7/B4）：拉载时序表适配固件 V1.2 默认 14 条。第 6/7 行时间默认 0.70→0.75（对齐固件 {110,75},{150,75}）；第 15 行默认清空+ToolTip（固件默认 14 条说明）；`TryParseLoadSequences` 重构为尾部空行感知——新增 `IsTailEmptyRow`（双空白或双 0=空行，方案页"0"未用行约定自洽；一空一有值=混合行单独报错），从后往前定 lastFilled，返回实际 count；两处组包（主页面+方案页应用）data[2]=seqCount 动态（14/15，数组 3+seqCount*4）；主页面回读 `UpdateLoadSequenceParams` 清空超出 count 行，方案页 `UpdateRecipeTextBoxes` 超出行回归"0"约定（对称封死陈旧值场景）。
验证：编译 0 警告 0 错误；文档步骤 7 四条验证用例（默认 count=14 且 6/7 条 75、填满 15、中间空行报错、回读残留清空）经审查员代码走查逐条闭合；附带改善——方案页预设配方 #5（后 10 行全 0）旧版无法应用、现正确截为 5 条。

**涉及位置**

- MainWindow.xaml:1304/1307（0.75）、1330-1335（第 15 行清空+ToolTip）
- MainWindow.xaml.cs:1549-1611（主页面组包）、2024-2118（IsTailEmptyRow+TryParseLoadSequences）、3463-3510（回读清空）、4395-4432（方案页组包）、4528-4566（方案页回读回归"0"）

**对应待办**

- H

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞；组包与固件 fcu_handle_set_load_params 861-888 逐字段核对匹配）。3 条建议全部落实（文案与 count 解耦、方案页补清行、混合行独立提示）。遗留风险：方案页量纲错配为预先存在问题（配方框原始值 vs 解析器按显示值×10），记入"其他"另开待办。

**Git Commit**

feat(H): 时序表适配固件14条默认——0.75s/第15行可空/count动态/回读清空 → f06e222

### 2026年09月21日（更新7）

**更新内容**

待办 G（实施文档步骤 6/B3）：0x0502 lock_coefficient 保留字段处理。tbx_LoadLockFactor 置灰+ToolTip（表头加"(保留)"）；组包改从 _lastLockFactor0P001 透传缓存取值（初值 900=固件默认，不再解析控件文本——与文档所述阀门页"隐藏框"模式形式偏差但更稳，审查确认功能等价）；回读 UpdateLoadControlParams 更新缓存并加 1..1000 合法域护栏（防畸形回读帧把缓存置 0 导致后续 0x0502 整帧被固件拒绝）。审查核验：fcu_control.c 全文零处 lock_coefficient 引用（最大功率锁定已改直接取时序电流），"保留字段"判断成立；全项目无第二条 0x0502 写路径。
验证：编译 0 警告 0 错误（审查员独立复现）。

**涉及位置**

- MainWindow.xaml:1357-1362（表头+置灰+ToolTip）
- MainWindow.xaml.cs:97-99（缓存字段）、1658-1700（SetLoadControlParamHelper 透传）、3466-3487（回读更新+护栏）

**对应待办**

- G

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞）。建议 1 已落实（护栏）；建议 2（1614-1659 五个未绑定孤儿 handler 死代码）不采纳——先于本任务存在，删除属独立清理事项记入"其他"。

**Git Commit**

feat(G): 0x0502锁定系数置灰为保留字段，组包透传缓存+回读护栏 → e46f57d

### 2026年09月21日（更新6）

**更新内容**

待办 F（实施文档步骤 5/B2）：压力参数 UI 默认值对齐固件 V1.2——_highPressureMin 300→100、_lowPressureMin 30→35、_lowPressureMax 80→65（固件 fcu_protocol.c:68/72/73）；三处 ToolTip 同步（高压改"启动门槛"语义：≥此值才允许启动，对照 fcu_control.c:439/783 用法）。回读链路（0x0101→UpdateViewModelPressureParams）未动，设备实际值仍覆盖默认展示。
验证：编译 0 警告 0 错误；全 XAML/C# 无压力旧值残留（审查员独立核对，区分占空比 30%/超时 300ms 等无关项）。

**涉及位置**

- ViewModels/MainViewModel.cs:268-277（默认值+注释）
- MainWindow.xaml:492/541/560（ToolTip）

**对应待办**

- F

**审查结果**

通过（第 1 轮，独立 Reviewer 零阻塞；数值逐条对照 fcu_protocol.c 源码核验）。建议不采纳 2 条（"高压最小值"字段名与"建议初值"措辞统一——文案统一类，记入"其他"防范围蔓延）。

**Git Commit**

feat(F): 压力默认值对齐固件V1.2——100/35/65+启动门槛语义ToolTip → a926312

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

feat(E): SOC参数适配——默认60/61、start<stop交叉校验、语义文案全量迁移 → 0defdac

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

feat(D): XCP升级状态机——加载指令/断电回退/连机/擦写固化/重启验证/归一化 → 463baa9

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

feat(C): 升级页切换OpenBLT/S-record链路，旧.upg路径UI下线（占位待D） → a0f6ee5

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

feat(B): OpenBLT XCP客户端，9命令+帧重组器，修复缓冲越界与错误帧误报 → 626a2f0

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

feat(A): S-record解析模型SrecFile，五条强校验+间隙0xFF合并 → 5b1a8e3
