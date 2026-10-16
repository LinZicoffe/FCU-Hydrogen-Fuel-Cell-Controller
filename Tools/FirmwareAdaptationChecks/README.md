# 固件主线适配离线检查

无需硬件；实际加载WPF资源/窗口和按钮事件，使用假串口替代传输，不打开真实窗口、不发送真实设备命令、不修改注册/许可证数据。

```powershell
dotnet run --project Tools/FirmwareAdaptationChecks/FirmwareAdaptationChecks.csproj -c Debug -- --ui
dotnet run --project Tools/FirmwareAdaptationChecks/FirmwareAdaptationChecks.csproj -c Release -- --ui
```

当前两配置均`PASS 214 checks`。不加`--ui`仅执行协议/会话/报警/S-record检查；加`--ui --render-dir <输出目录>`可保存1280×780离线页面呈现（假数据，不能作为实机截图）。

新增0302检查：文档完整CRC帧、float32大端/负偏移/有限范围、ACK后回读、旧0301布局、错误/超时/不支持、实际k/b按钮及输入校验、独占共享通道、升级互斥、Dispatcher代数与目标/断线清理。不会写入真实设备或自动拟合系数。

覆盖：固定整帧CRC、大小端和负电流、长度/地址/CRC拒绝、碎片接收、6秒缓存年龄续算、超时与停止、旧1B IAP排除；0x0902边界/零值/非法输入、OFF忙/保存失败等错误、共享串口独占、回读匹配/不匹配、不支持旧固件、静默窗口和Epoch；原0x0202/0x0901报文回归；直接诊断接管/保存owner归还/泵送重入；升级准备与S8保锁、最终连接抑制清理；看门狗报警原因；真实S-record错误基址/占位拒绝及兼容镜像接受。

检查入口失败会抛出带名称的异常，进程退出1；成功退出0。只读与模拟器测试不能证明Flash掉电持久化、物理RS485冲突、引脚极性、保护动作时延或真实OpenBLT升级。

运行时创建的S-record夹具位于检查程序bin目录，结束时按确定文件名删除；不读取或修改真实固件镜像。页面呈现不运行Application启动注册流程。
