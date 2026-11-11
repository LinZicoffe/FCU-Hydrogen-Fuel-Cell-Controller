# 软件进入 Bootloader 升级适配功能更新日志

> 批次日期：2026-11-10
> 分支：`feature/bl-software-entry-adapt`（基于 master `b334b37`）
> 配套固件（**待测版**）：450V_FCU_NewPCB `bootloader-test-20261030-01` 发布版
> - 发布页：https://github.com/shimu-huang666/450V_FCU_NewPCB/releases/tag/bootloader-test-20261030-01
> - APP 源码：`99aedb7a4b04f88403ec4b130dead7be577275e1`
> - BL 源码（Boot-For-FCU）：`169cdf0fcc1d87c3b2bb937c29156e88b69c36ce`
> 约束：仅修改上位机；不修改嵌入式固件与已有发布附件；不重写 XCP 下载协议。

## 1. 背景与缺口

上位机升级页已有完整 OpenBLT XCP 下载链路（CONNECT→擦除→写入→固化→重启→APP 探活，
`OpenBltXcpClient`，57600bps），且发布版 S-record 镜像可被上位机解析器接受。
唯一缺口在**进入 BL**：上位机不发送软件升级请求，仅切换波特率后循环 XCP CONNECT，
要求用户断电重新上电（依赖 BL 普通上电 3 秒连接窗口）。

待测版固件补齐了软件入口：

| 接口项 | 发布版源码核实结果 |
|---|---|
| 请求 | AA55 帧 `0x12`，数据域 1B 确认字节 `0x01`，**仅单播、仅完全停机 OFF**（`fcu_protocol.h:85`、`fcu_handle_enter_bootloader()`；广播静默拒绝） |
| 应答 | `0x92`，数据域 1B 错误码：`0x00` 接受 / `0x02` 确认值错误(FCU_ERR_PARAM_INVALID) / `0x03` 非OFF或设备忙(FCU_ERR_DEVICE_BUSY)（`fcu_boot_request()`：`stack_state != FCU_CTRL_STATE_OFF` 即 0x03） |
| 时序 | 固件**先发应答**，等发送完成（上限 1s）→ 关执行器安全输出 → 写 TAMP 备份寄存器一次性升级标记（双字含反码，写失败 `Error_Handler` 不复位）→ 软复位（`fcu_boot.c`） |
| BL 行为 | 校验标记（软复位来源 + 双字）后**常驻等待 XCP，不依赖普通上电 3 秒窗口**；普通上电仍 3s 窗口后跳 APP（`fcu_bl.c`、`FCU_BOOT_ENTRY_WINDOW_MS=3000`） |
| 波特率 | APP 业务 9600 8N1（`usart.c:42`）；BL 下载 57600 8N1（`blt_conf.h:98`） |
| 示例帧 | 地址 1 请求 `AA 55 01 12 01 01 27 F8`、成功应答 `AA 55 01 92 01 00 0C 83`（CRC16-CCITT 大端，已独立复算冻结） |
| 镜像契约 | `FCU_BOOT_APP_ADDRESS=0x08003000`、校验字 `0x1D8`、参数页不擦除——与上位机既有 SrecFile 守卫一致，**零改动** |

注意：`0x11/0x91` 在本待测版**仍是 BMS 缓存查询**，不是升级入口；历史
`FUNC_ENTER_IAP(0x11)/RESP_ENTER_IAP(0x91)` 旧值不复用。

## 2. 修改内容

### 2.1 协议常量（`Protocol/ProtocolConstants.cs`）

新增 `FUNC_ENTER_BOOTLOADER = 0x12`、`RESP_ENTER_BOOTLOADER = 0x92`、
`ENTER_BL_CONFIRM = 0x01`、`ENTER_BL_ACK_TIMEOUT_MS = 2000`（固件应答 8B@9600≈8ms，
内部 TX 完成守卫上限 1s，主机取 2 倍裕量），并登记 `GetFunctionCodeDescription` 条目。

### 2.2 会话类（`Services/EnterBootloaderSession.cs`，新增）

仿 `BmsCacheSession` 模式（send/clock 委托可注入，离线可测）：

- `Request(address)`：经 `ProtocolHandler.BuildRequestFrame` 组帧（地址参与 CRC，复用现有
  组帧与校验实现），**单次发送不重发**（固件接受后即复位，重发只会再次触发标记写入）；
  等待期间重复调用被拒。
- `Receive(frame)`：严格核对帧长 8 / 地址 / 功能码 0x92 / 数据长度 1 后读取错误码；
  非本事务帧（心跳/主动上报/他机应答/畸形 0x92）一律丢弃继续等待。CRC 由
  `ProtocolHandler` 完整帧链路校验，会话不重复实现。
- `Tick()`：2000ms 应答超时 → `Timeout` 终态（绝不当作成功）。
- 终态：`Accepted / Rejected(ErrorCode) / SendFailed / Timeout`。

### 2.3 升级页（`Views/FirmwareUpgradeView.xaml(.cs)`）

`RunUpgradeAsync` 步骤③ APP 在线分支由「提示断电重启 + 盲轮询 CONNECT」改为：

1. **OFF 门控**：前置检查 0x87 读到的电堆状态非 0（OFF）则直接失败停止，不发送 0x12
   （FAULT=3 同样拦截——软件入口与固件 OFF 限制一致；旧版允许 OFF/FAULT 仅适用于断电路径）。
2. 发送 0x12（单播 + 确认字节 0x01），`AwaitEnterBootloaderAck` 驱动等待：
   - `Accepted`（0x92/0x00）→ 走既有链路：`SetBlMode(true)` → `ReopenSerial(57600)` →
     `_client.Reset()` 清接收残态 → CONNECT 轮询（BL 已常驻等待，无 3 秒窗口竞态）→
     擦除/写入/固化/重启/探活全流程不变。
   - `Rejected` → 准确文案停止（0x03=非OFF或设备忙 / 0x02=确认值错误）。
   - `SendFailed` → 提示串口发送失败并停止。
   - `Timeout` → 提示"设备可能为不支持软件进入的旧固件或应答丢失"并停止，
     **绝不当作成功继续擦写**；文案给出三条恢复路径（见 §3）。
3. 取消：等待中取消经 `OperationCanceledException` 上抛，落入既有"已取消"分支。
4. APP 不在线分支（0x07 无应答：BL 常驻/首次安装/升级中断）**原样保留**，
   继续直接探测 Bootloader——软件进入不成为所有升级路径的前提。

界面文案同步：步骤③/②描述、开始确认弹窗、页面蓝色须知与橙色旧固件说明
（明确本待测版支持软件进入；旧固件按断电/SWD 处置）。

### 2.4 帧分发（`MainWindow.xaml.cs`）

`OnFrameReceived` 新增 `case RESP_ENTER_BOOTLOADER`：该帧在方法开头已经
`BlAppFrameReceived` 完整帧广播交给升级页会话消费，此处仅拦截"未知响应码"提示，
不做处理也不转交其它事务（升级前置模式下常规事务均已挂起，且 0x92 不匹配任何
既有会话守卫分支 0x82/0x8A/0x8E/0x91）。

### 2.5 不改动项（回归保证）

- `OpenBltXcpClient.cs`、`SrecFile.cs`（镜像地址 0x08003000 / 校验字 0x1D8 / 参数页
  保护）零改动；XCP 下载协议、心跳静默、退出归一化逻辑零改动。
- BMS 缓存查询 0x11/0x91 语义与处理零改动。
- `TryPrepareUpgrade` 互斥清单、轮询挂起、普通发送拦截（`_upgradePrecheckMode`/
  `_blUpgradeMode`）沿用既有机制，未新增旁路。

## 3. 失败与恢复口径

| 场景 | 行为 | 恢复路径 |
|---|---|---|
| 非 OFF（含 FAULT） | 不发送 0x12，明确提示先关机 | 关机后重新点开始 |
| 0x92/0x03 拒绝 | 停止，提示"非OFF / 上一请求处理中 / 固件正在上报数据占用发送通道"（对应固件三个触发分支） | 稍候或关机后重试 |
| 0x92/0x02 | 停止，提示确认值校验失败（不应发生） | 重试；复现则核对固件版本 |
| 发送失败 | 停止，提示串口发送失败 | 检查串口重试 |
| 等待超时（旧固件无 0x12 / 应答丢失） | 停止，绝不进入擦写 | ① 若应答丢失设备已进 BL：重连 9600 串口后重点"开始"，0x07 不再应答，自动探测引导续升（退出后串口已关闭）；② 已预烧兼容引导的旧固件：先断电，重连后点开始，待状态栏出现"探测 Bootloader"（前置检查约 7 秒完成）再重新上电（30 秒内）走 3 秒连接窗口——过早上电会再次误入软件请求分支；③ 旧独立 main(@0x08000000) 镜像仍需 SWD |
| 等待中取消 | "已取消"分支，退出归一化 | 重新升级即可 |
| APP 本就不应答（BL 常驻/首次安装/中断后） | 原有直接探测路径，不受影响 | 既有能力 |

## 4. 验证

- Debug 构建：0 警告 0 错误。
- 离线回归（`Tools/FirmwareAdaptationChecks --ui`）：**PASS 355 项**（原 327 + 新增 28 项
  `EnterBootloaderChecks`：金样请求/应答帧逐字节、地址变化重算 CRC、单发不重发、
  错误地址/错误功能码/畸形长度/主动上报帧不误收、0x02/0x03 拒绝错误码、超时边界与
  迟到应答不复活、发送失败立即终态、分片喂入完整帧链路、坏 CRC 帧到达不了会话、
  0x11/0x91 旧 IAP 值不混淆）。
- **真机升级待验证**（离线无法覆盖项见 `docs/实施步骤-软件进入Bootloader升级适配.md` §7）。

## 5. 遗留与板测清单

见实施步骤文档 §7（待板测项 6 条：软件进入全流程、应答丢失重试路径、旧固件超时
提示与断电窗口路径、取消归一化、BL 常驻续升、多帧干扰）。
