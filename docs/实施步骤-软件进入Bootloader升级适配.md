# 实施步骤：软件进入 Bootloader 升级适配（待测版固件 bootloader-test-20261030-01）

> 适用对象：上位机《FCU 氢燃料电池控》（.NET 8 WPF）
> 固件依据：450V_FCU_NewPCB @ `99aedb7`（tag `bootloader-test-20261030-01`），
> Boot-For-FCU @ `169cdf0`（https://github.com/shimu-huang666/Boot-For-FCU）
> 约束：仅修改上位机；不修改嵌入式固件或已有发布附件；不重写下载协议；
> 不扩展心跳等功能；保持镜像地址、校验字与参数页保护。
> 前置：先核对固件接口（本文 §2），禁止盲目修改。

## 1. 目标

APP 在线且完全停机 (OFF) 时，上位机自动发送软件升级请求使 FCU 进入 Bootloader，
免去"断电重新上电"人工步骤；所有失败路径准确提示并停止，不误入擦写；
既有 APP 无应答 / BL 常驻探测与手动恢复路径保持原样。

## 2. 接口核对结论（以发布版源码为准）

| 项 | 核对位置（APP@99aedb7 / BL@169cdf0） | 结论 |
|---|---|---|
| 功能码 0x12 请求、确认字节 0x01、仅单播 | `App/Inc/fcu_protocol.h:85`；`App/Src/fcu_protocol.c fcu_handle_enter_bootloader()`（广播 return 0 静默；`request[5]==0x01` 才受理） | ✅ |
| 响应 0x92、数据域 1B、帧布局 | `fcu_protocol.h:105`；`fcu_build_set_response()`：`AA 55 addr 92 01 err CRC(H,L)` 共 8B | ✅ |
| 错误码 0x02/0x03 | `FCU_ERR_PARAM_INVALID=0x02`；`FCU_ERR_DEVICE_BUSY=0x03`（`fcu_boot_request()`：非 OFF / 发送忙 / 重复请求三分支） | ✅ |
| OFF 枚举 | `fcu_control.h:37 FCU_CTRL_STATE_OFF=0`（FAULT=3）；0x87 应答数据域首字节为工作状态 | ✅ |
| 应答后时序 | `fcu_boot.c`：应答异步发送 → `fcu_boot_on_tx_complete` → 安全输出 → TAMP 标记（BKP0R/BKP1R 双字含反码，写失败 Error_Handler 不复位）→ `NVIC_SystemReset`；TX 完成守卫 `FCU_RS_485_RESPONSE_TIMEOUT_MS=1000` | ✅ |
| BL 常驻 | `App/fcu_bl.c`：软复位来源 + 标记校验通过 → `s_bl_upgrade_requested` → 永不 `CpuStartUserProgram()`，常驻等待 XCP；普通上电 3s 窗口（`FCU_BOOT_ENTRY_WINDOW_MS=3000`）保留 | ✅ |
| 波特率 | APP 9600 8N1（`Core/Src/usart.c:42`）；BL 57600 8N1（`App/blt_conf.h:98`） | ✅ |
| 示例帧 CRC | `AA 55 01 12 01 01 → 27 F8`、`AA 55 01 92 01 00 → 0C 83`（CRC16-CCITT init 0xFFFF poly 0x1021 大端，独立复算） | ✅ |
| 0x11/0x91 | 本待测版仍是 BMS 缓存查询（18B），**不是**升级入口；不复用历史 FUNC_ENTER_IAP 旧值 | ✅ |
| 镜像契约 | `fcu_boot_contract.h`：APP@0x08003000、校验字 0x1D8——与上位机 SrecFile 守卫一致，无需改动 | ✅ |

## 3. 修改步骤（实际实施顺序）

1. **协议常量**（`Protocol/ProtocolConstants.cs`）
   `FUNC_ENTER_BOOTLOADER=0x12`、`RESP_ENTER_BOOTLOADER=0x92`、`ENTER_BL_CONFIRM=0x01`、
   `ENTER_BL_ACK_TIMEOUT_MS=2000`（2×固件 TX 守卫）；登记功能码描述。
2. **会话类**（`Services/EnterBootloaderSession.cs` 新增）
   `Request/Receive/Tick/Reset` + 终态枚举 `Accepted/Rejected/SendFailed/Timeout`。
   组帧复用 `ProtocolHandler.BuildRequestFrame`（地址参与 CRC 自动重算）；
   单次发送不重发；`Receive` 严格核对 帧长8/地址/0x92/长度1/错误码，
   其余帧丢弃继续等待；send/clock 委托注入支持离线检查。
3. **升级页**（`Views/FirmwareUpgradeView.xaml.cs`）
   - 前置检查保存 `appStackState`（0x87 数据域首字节）。
   - 步骤③ APP 在线分支：OFF 门控（非 0 不发送，FAULT=3 提示先处理故障关机）→
     `AwaitEnterBootloaderAck`（发 0x12 + 驱动等待至终态；取消上抛 OCE）→
     按终态分流：Accepted 继续 / 其余三种 UpgradeException 停止并给准确文案。
   - 成功后走**原有**链路：`SetBlMode(true)` → `ReopenSerial(57600)` → `_client.Reset()`
     （清 APP 在途上报残态）→ CONNECT 轮询 30s → 擦除/写入/固化/重启/探活。
   - 0x07 无应答分支（BL 常驻/首次安装/升级中断）保持原样直接探测。
4. **帧分发**（`MainWindow.xaml.cs OnFrameReceived`）
   新增 `case RESP_ENTER_BOOTLOADER` 拦截未知码提示（帧已先行经
   `BlAppFrameReceived` 交给升级页会话核验消费）。
5. **界面文案**（`.xaml` + `ResetSteps` + 开始确认弹窗）
   步骤②③描述、蓝色须知（待测版软件进入 + 仍需 SWD 预烧引导）、
   橙色旧固件说明（旧固件断电窗口 / SWD 口径）。
6. **离线检查**（`Tools/FirmwareAdaptationChecks/EnterBootloaderChecks.cs` 新增，28 项，
   接入 `Program.Main`）：见 §6。

## 4. 修改前后流程对比

```
修改前（APP 在线）:                          修改后（APP 在线，待测版）:
 前置检查 0x07 (OFF/FAULT)                    前置检查 0x07，保存状态
 → 弹窗"请断电, 3 秒后重新上电"               → 仅 OFF 继续；非 OFF(含FAULT) 停止+提示
 → SetBlMode(true) 切 57600                   → 发 0x12(确认0x01)，等 0x92 严格核对(≤2s)
 → 循环 CONNECT (赌 3s 上电窗口)              ├─ 0x00 → SetBlMode(true) 切 57600 → CONNECT 轮询
                                              │         (BL 常驻等待，无窗口竞态) → 下载全流程
                                              ├─ 0x02/0x03 → 停止+准确提示
                                              ├─ 发送失败 → 停止+提示
                                              └─ 超时 → 停止(绝不擦写)+恢复指引
 APP 不在线分支: 两条完全相同（直接探测 BL，保留）
```

## 5. 资源与互斥沿用

`TryPrepareUpgrade`（全部参数事务/轮询/普通发送互斥清单）→ 握手期间 AA55 通道独占；
`SendAppFrame` 在 `_blUpgradeMode` 下自动拒绝；成功/失败/取消/异常四条退出路径均走
既有 `finally`（`SetBlMode(false)` 解锁 + 按钮恢复）与 `ExitNormalizationAsync`
（软件进入失败时 `blModeEntered=false`，跳过 6s 静默，9600 探活归一化）。

## 6. 离线验证

- `dotnet build` Debug：0 警告 0 错误。
- `dotnet run --project Tools/FirmwareAdaptationChecks -c Debug -- --ui`：
  **PASS 355**（327 既有 + 28 新增）。新增覆盖：
  - 请求/应答金样帧逐字节（固件文档示例，CRC 独立冻结）；地址变化重算 CRC；
  - 单发不重发；错误地址/错误功能码(0x91 缓存、0x87、0xF1 上报)/畸形长度不误收；
  - 0x02/0x03 拒绝错误码透出；超时边界(1999/2000ms)、迟到应答不复活超时会话；
  - 发送失败立即终态；分片喂入 ProtocolHandler 完整帧链路；坏 CRC 帧到不了会话；
  - 0x11/0x91 旧 IAP 值不混淆（FUNC_ENTER_IAP 仍为 0x11）。
- 取消路径：`AwaitEnterBootloaderAck` 取消上抛 OCE → 既有"已取消"分支（代码审查核验，
  离线 UI 检查不覆盖升级页后台任务，已在 §7 板测）。

## 7. 待板测清单（真机验证，当前为"离线验证通过，真机升级待验证"）

1. 待测版固件 OFF 态软件进入全流程：0x12 → 0x92/0x00 → 自动切 57600 → CONNECT →
   擦除/写入/固化 → 重启探活（一次成功）。
2. 应答丢失场景：接受但主机未收到 0x92 → 超时停止 → 重新点开始 → 0x07 无应答 →
   自动探测 BL 续升（验证恢复指引①）。
3. 旧固件（无 0x12）在线：超时提示文案与停止行为；断电后点开始 + 30s 内上电走
   3s 窗口（验证恢复指引②）。
4. 非 OFF（运行/故障）态：不发 0x12 直接提示；人为制造 0x03（快速连点/主动上报占用 TX）核对拒绝文案（三分支口径）。
5. 等待 0x92 期间取消：按钮/互斥/串口状态恢复正确（9600 保持、可正常断开）。
6. 升级中断后 BL 常驻续升 + 多帧干扰（心跳/上报在握手期间到达不误判）。
7. 超时恢复指引①②实测：① 应答丢失→重连 9600 重试自动探测续升（含退出后串口确已关闭）；② 旧固件断电→重连点开始→**待状态栏出现"探测 Bootloader"再上电**（过早上电复现误入 0x12 分支）。

## 8. 关联文档

- 根级：`软件进入Bootloader升级适配功能更新日志.md`
- 评审：`docs/评审记录-软件进入Bootloader升级适配.md`
- 前序：`docs/实施步骤-OpenBLT升级与固件适配.md`（下载协议链路，本批次未改）
