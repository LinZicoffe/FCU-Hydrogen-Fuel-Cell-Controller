using System;
using System.Collections.Generic;
using System.Windows.Threading;
using FCU_氢燃料电池控.Managers;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// BMS Modbus 主站服务 (V1.28-S)
    /// 创建日期: 2026-08-06
    /// 用途: 03H 轮询 reg128~152 + 10H MOS 控制; 与 AA55 共用串口字节流
    /// [上位机优化计划 h-t2] 线程模型: 全部方法在 UI 线程调用(DispatcherTimer 驱动)
    /// </summary>
    public class BmsService : IDisposable
    {
        private readonly SerialPortManager _serial;

        /// <summary>Modbus 接收累积缓冲(与 AA55 状态机并行喂入)</summary>
        private readonly List<byte> _rxAccum = new List<byte>(512);

        private enum State { Idle, WaitingRead, WaitingWrite }
        private State _state = State.Idle;

        private DispatcherTimer? _pollTimer;
        private DispatcherTimer? _responseTimer;   // 一次性, 响应超时 300ms
        private DispatcherTimer? _retryTimer;      // 一次性, 总线静默重试 30ms
        private byte[]? _pendingWriteReq;          // 10H 请求, 用于回显校验
        private DateTime _lastRxTime = DateTime.MinValue;
        private int _timeoutCount;

        /// <summary>寄存器起始 (reg128=电流) 与个数 (128~152 共25个)</summary>
        private const ushort REG_START = 128;
        private const ushort REG_COUNT = 25;

        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan BusSilentRequired = TimeSpan.FromMilliseconds(20);
        private const int OfflineThreshold = 3;

        /// <summary>BMS 特殊功能寄存器 [BMS协议 1.28-S §3.3]</summary>
        private const ushort REG_CHARGE_MOS = 1;
        private const ushort REG_DISCHARGE_MOS = 2;
        private const ushort MOS_ON = 0xAAAA;
        private const ushort MOS_OFF = 0x5555;

        /// <summary>
        /// BMS 从站地址 (UI 可配, 默认1, 与固件 FCU_MODBUS_SLAVE_ADDR 一致)
        /// </summary>
        public byte SlaveAddress { get; set; } = 1;

        /// <summary>
        /// AA55 帧接收是否空闲的判定回调（由 MainWindow 注入 ProtocolHandler.IsIdle）。
        /// 与固件 fcu_modbus.c 的三条件仲裁对齐：使能 + 接收状态机空闲 + 总线静默。
        /// 缺少此条件时，Modbus 请求可能在 AA55 帧半接收状态下插入，
        /// 被状态机吃进帧体导致该 AA55 帧 CRC 失败。
        /// 未注入(null)时退化为原有的两条件判定。
        /// </summary>
        public Func<bool>? IsAa55Idle { get; set; }

        /// <summary>
        /// 上位机自身 AA55 请求是否在途的判定回调（由 MainWindow 注入 _awaitingStackInfo）。
        /// [审查修复 P1②] 静默+空闲两条件看不到"请求已发出、响应未回来"的窗口：
        /// 0x07 发出后约 48ms 内 0x87 响应占线，此窗口内发 Modbus 请求会与 FCU 发送相撞
        /// （双端同时驱动 RS485，双帧俱毁）。未注入(null)时跳过该条件。
        /// </summary>
        public Func<bool>? IsAa55RequestInFlight { get; set; }

        /// <summary>
        /// BMS 事务是否在途（含响应等待与 30ms 重试挂起）。
        /// [审查修复 P1②] 供电堆信息轮询发送前反查，避免两个方向的碰撞窗口。
        /// </summary>
        public bool IsBusy => _state != State.Idle || _retryTimer != null;

        /// <summary>最新一帧解析结果</summary>
        public BmsData Data { get; } = new BmsData();

        /// <summary>是否正在自动轮询</summary>
        public bool IsPolling => _pollTimer?.IsEnabled == true;

        /// <summary>数据更新事件(成功解析或离线状态变化)</summary>
        public event Action<BmsData>? DataUpdated;

        /// <summary>状态消息事件</summary>
        public event Action<string>? StatusChanged;

        /// <summary>错误消息事件</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>MOS 写完成事件 (success, message)</summary>
        public event Action<bool, string>? MosWriteCompleted;

        public BmsService(SerialPortManager serial)
        {
            _serial = serial;
        }

        #region 接收喂入与解析

        /// <summary>
        /// 并行喂入串口原始字节流 (MainWindow.SerialPort_DataReceived 调用)
        /// </summary>
        public void AppendReceivedBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;

            _lastRxTime = DateTime.Now;
            _rxAccum.AddRange(bytes);

            // 防止长期无主时缓冲无限增长
            if (_rxAccum.Count > 4096)
            {
                _rxAccum.RemoveRange(0, _rxAccum.Count - 256);
            }

            if (_state != State.Idle)
            {
                TryParse();
            }
        }

        private void TryParse()
        {
            byte func = (_state == State.WaitingRead)
                ? ModbusRtu.FUNC_READ_HOLDING
                : ModbusRtu.FUNC_WRITE_MULTIPLE;

            var result = ModbusRtu.TryParseResponse(
                _rxAccum.ToArray(), _rxAccum.Count, SlaveAddress, func, REG_COUNT,
                out byte[]? frame, out int consumed, out byte exceptionCode);

            if (consumed > 0)
            {
                _rxAccum.RemoveRange(0, consumed);
            }

            switch (result)
            {
                case ModbusRtu.ParseResult.Frame:
                    StopResponseTimer();
                    if (_state == State.WaitingRead)
                    {
                        FillData(frame!);
                        _timeoutCount = 0;
                        if (!Data.Online)
                        {
                            Data.Online = true;
                            StatusChanged?.Invoke("BMS 已上线");
                        }
                        DataUpdated?.Invoke(Data);
                    }
                    else
                    {
                        VerifyWriteEcho(frame!);
                    }
                    _state = State.Idle;
                    break;

                case ModbusRtu.ParseResult.Exception:
                    StopResponseTimer();
                    string excText = GetExceptionText(exceptionCode);
                    if (_state == State.WaitingRead)
                    {
                        ErrorOccurred?.Invoke($"BMS 异常响应: {excText}");
                        IncrementTimeout();
                    }
                    else
                    {
                        MosWriteCompleted?.Invoke(false, $"BMS 拒绝写入: {excText}");
                    }
                    _state = State.Idle;
                    break;

                // None / NeedMoreData: 继续等待, 超时由 _responseTimer 兜底
            }
        }

        /// <summary>
        /// 填充 BmsData (03H 响应帧, 数据域 reg128+i 大端)
        /// [BMS协议 1.28-S §3.1]
        /// </summary>
        private void FillData(byte[] frame)
        {
            Data.CurrentRaw = (short)ModbusRtu.GetRegister(frame, 0);        // reg128 S16
            Data.PackVoltageRaw = ModbusRtu.GetRegister(frame, 1);           // reg129
            Data.Soc = ModbusRtu.GetRegister(frame, 2);                      // reg130
            Data.Soh = ModbusRtu.GetRegister(frame, 3);                      // reg131
            Data.RemainCapacityRaw = ModbusRtu.GetRegister(frame, 4);        // reg132
            Data.FullCapacityRaw = ModbusRtu.GetRegister(frame, 5);          // reg133
            Data.RatedCapacityRaw = ModbusRtu.GetRegister(frame, 6);         // reg134
            Data.CycleCount = ModbusRtu.GetRegister(frame, 7);               // reg135
            // reg136 预留, 跳过
            Data.AlarmStatus = ModbusRtu.GetRegister(frame, 9);              // reg137 附表1
            Data.ProtectStatus = ModbusRtu.GetRegister(frame, 10);           // reg138 附表2
            Data.FaultStatus = ModbusRtu.GetRegister(frame, 11);             // reg139 附表3
            Data.SystemStatus = ModbusRtu.GetRegister(frame, 12);            // reg140 附表4
            Data.SwitchStatus = ModbusRtu.GetRegister(frame, 13);            // reg141 附表5
            // reg142~144 空洞/特殊, 跳过
            Data.SeriesCells = ModbusRtu.GetRegister(frame, 17);             // reg145
            Data.CellVoltMaxMv = ModbusRtu.GetRegister(frame, 18);           // reg146
            Data.CellVoltMinMv = ModbusRtu.GetRegister(frame, 19);           // reg147
            Data.CellTempCount = ModbusRtu.GetRegister(frame, 20);           // reg148
            Data.CellTempMaxRaw = (short)ModbusRtu.GetRegister(frame, 21);   // reg149 S16
            Data.CellTempMinRaw = (short)ModbusRtu.GetRegister(frame, 22);   // reg150 S16
            Data.MosTempRaw = (short)ModbusRtu.GetRegister(frame, 23);       // reg151 S16
            Data.EnvTempRaw = (short)ModbusRtu.GetRegister(frame, 24);       // reg152 S16
            Data.LastUpdate = DateTime.Now;
        }

        /// <summary>
        /// 10H 回显校验: 响应8B中 regstart(2)+count(2) 须与请求一致
        /// [BMS协议 1.28-S §2.2]
        /// </summary>
        private void VerifyWriteEcho(byte[] frame)
        {
            bool ok = _pendingWriteReq != null
                      && frame[2] == _pendingWriteReq[2]
                      && frame[3] == _pendingWriteReq[3]
                      && frame[4] == _pendingWriteReq[4]
                      && frame[5] == _pendingWriteReq[5];

            MosWriteCompleted?.Invoke(ok, ok ? "MOS 控制写入成功" : "MOS 回显校验失败");
            _pendingWriteReq = null;
        }

        #endregion

        #region 轮询控制

        /// <summary>
        /// 启动自动轮询
        /// </summary>
        /// <param name="intervalSeconds">轮询周期(秒), 默认2s与固件节奏一致</param>
        public void StartPolling(double intervalSeconds = 2.0)
        {
            StopPolling();
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            _pollTimer.Tick += (s, e) => RequestRead();
            _pollTimer.Start();
            RequestRead();   // 立即读一次
        }

        /// <summary>
        /// 停止自动轮询。彻底复位: 响应/重试定时器与状态机一并停止,
        /// 防断开/进升级后 300ms 内仍弹出陈旧的"BMS 响应超时"提示;
        /// Online 与超时计数同步复位, 防跨会话残留(上个会话末次的离线态带入下个会话)。
        /// [审查修复 P1②/P3]
        /// </summary>
        public void StopPolling()
        {
            _pollTimer?.Stop();
            _pollTimer = null;
            // 写在途被停时给 UI 一条明确反馈, 避免"MOS 写入结果未知"静默丢弃 (审查建议)
            if (_state == State.WaitingWrite)
            {
                StopResponseTimer();
                _pendingWriteReq = null;
                MosWriteCompleted?.Invoke(false, "轮询已停止, MOS 写入结果未知");
            }
            StopResponseTimer();
            _retryTimer?.Stop();
            _retryTimer = null;
            _state = State.Idle;
            _pendingWriteReq = null;
            _timeoutCount = 0;
            Data.Online = false;
        }

        /// <summary>手动读取一次</summary>
        public void ReadOnce() => RequestRead();

        #endregion

        #region MOS 控制 (10H)

        /// <summary>充电 MOS 控制 (reg1, 0xAAAA开/0x5555关)</summary>
        public void SetChargeMos(bool on) => RequestWrite(REG_CHARGE_MOS, on);

        /// <summary>放电 MOS 控制 (reg2, 0xAAAA开/0x5555关)</summary>
        public void SetDischargeMos(bool on) => RequestWrite(REG_DISCHARGE_MOS, on);

        private void RequestWrite(ushort reg, bool on)
        {
            if (!_serial.IsOpen)
            {
                MosWriteCompleted?.Invoke(false, "串口未打开");
                return;
            }
            if (_state != State.Idle)
            {
                MosWriteCompleted?.Invoke(false, "总线忙, 请稍候");
                return;
            }
            // 与读请求同样的仲裁条件，避免在 AA55 帧半接收状态下插入 Modbus 写
            if (!IsBusAvailable())
            {
                MosWriteCompleted?.Invoke(false, "总线忙(FCU 通信中), 请稍候重试");
                return;
            }

            byte[] req = ModbusRtu.BuildWriteSingleReg(SlaveAddress, reg, on ? MOS_ON : MOS_OFF);
            _pendingWriteReq = req;
            _state = State.WaitingWrite;
            StartResponseTimer();

            if (!_serial.SendData(req))
            {
                StopResponseTimer();
                _state = State.Idle;
                _pendingWriteReq = null;
                MosWriteCompleted?.Invoke(false, "串口发送失败");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 发起一次 03H 读 (带总线静默检查, 降与 FCU 流量相撞概率 [R5])
        /// </summary>
        private void RequestRead()
        {
            if (!_serial.IsOpen)
            {
                return;
            }
            if (_state != State.Idle)
            {
                return;   // 上一次尚未完成, 本轮跳过
            }

            // 仲裁条件: 总线静默 ≥20ms 且 AA55 接收状态机空闲 (与固件三条件对齐)
            // 任一不满足则推迟30ms重试一次, 仍忙则放弃本轮
            if (!IsBusAvailable())
            {
                if (_retryTimer != null)
                {
                    return;   // 已有一次重试排队
                }
                _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                _retryTimer.Tick += (s, e) =>
                {
                    _retryTimer?.Stop();
                    _retryTimer = null;
                    if (IsBusAvailable())
                    {
                        SendReadFrame();
                    }
                };
                _retryTimer.Start();
                return;
            }

            SendReadFrame();
        }

        /// <summary>
        /// 总线是否可发起 Modbus 请求。
        /// 条件一: 距上次收到任意字节 ≥ BusSilentRequired(20ms)
        /// 条件二: AA55 帧接收状态机处于空闲 (未注入回调时跳过该条件)
        /// </summary>
        private bool IsBusAvailable()
        {
            if ((DateTime.Now - _lastRxTime) < BusSilentRequired)
            {
                return false;
            }

            // AA55 半接收状态下发 Modbus 请求，会让那一帧 AA55 吃进异物而 CRC 失败
            if (IsAa55Idle != null && !IsAa55Idle())
            {
                return false;
            }

            // [审查修复 P1②] 上位机自身 AA55 请求在途（0x07 已发未收）: 响应占线窗口内
            // 发 Modbus 请求会与 FCU 的 0x87 发送相撞
            if (IsAa55RequestInFlight != null && IsAa55RequestInFlight())
            {
                return false;
            }

            return true;
        }

        private void SendReadFrame()
        {
            if (_state != State.Idle || !_serial.IsOpen)
            {
                return;
            }

            byte[] req = ModbusRtu.BuildReadHoldingRegs(SlaveAddress, REG_START, REG_COUNT);
            _state = State.WaitingRead;
            StartResponseTimer();

            if (!_serial.SendData(req))
            {
                StopResponseTimer();
                _state = State.Idle;
                ErrorOccurred?.Invoke("BMS 请求发送失败(串口未打开)");
            }
        }

        private void StartResponseTimer()
        {
            StopResponseTimer();
            _responseTimer = new DispatcherTimer { Interval = ResponseTimeout };
            _responseTimer.Tick += OnResponseTimeout;
            _responseTimer.Start();
        }

        private void StopResponseTimer()
        {
            if (_responseTimer != null)
            {
                _responseTimer.Stop();
                _responseTimer.Tick -= OnResponseTimeout;
                _responseTimer = null;
            }
        }

        private void OnResponseTimeout(object? sender, EventArgs e)
        {
            StopResponseTimer();
            if (_state == State.WaitingRead)
            {
                IncrementTimeout();
                StatusChanged?.Invoke($"BMS 响应超时 ({_timeoutCount}/{OfflineThreshold})");
            }
            else if (_state == State.WaitingWrite)
            {
                _pendingWriteReq = null;
                MosWriteCompleted?.Invoke(false, "MOS 写入响应超时");
            }
            _state = State.Idle;
        }

        /// <summary>
        /// 超时计数, 连续 OfflineThreshold 次置离线 (仿固件宽松策略)
        /// </summary>
        private void IncrementTimeout()
        {
            _timeoutCount++;
            if (_timeoutCount >= OfflineThreshold && Data.Online)
            {
                Data.Online = false;
                ErrorOccurred?.Invoke("BMS 离线 (连续3次无响应)");
                DataUpdated?.Invoke(Data);   // 通知 UI 显示离线
            }
        }

        private static string GetExceptionText(byte code)
        {
            return code switch
            {
                1 => "无效功能码",
                2 => "无效寄存器",
                3 => "无效数据",
                4 => "校验码错误",
                5 => "写入命令无效",
                6 => "无效记录序号",
                _ => $"未知错误码({code})"
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            StopPolling();
            StopResponseTimer();
            _retryTimer?.Stop();
            _retryTimer = null;
        }

        #endregion
    }
}
