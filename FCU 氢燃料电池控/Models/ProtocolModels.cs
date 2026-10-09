using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// 电堆信息数据结构（26字节）
    /// 对应协议4.6节
    /// </summary>
    public class StackInfoData
    {
        /// <summary>
        /// 电堆工作状态 (0:关机, 1:启动中, 2:运行, 3:故障, 4:关机中)
        /// </summary>
        public byte WorkState { get; set; }

        /// <summary>
        /// 高压压力 (KPA)
        /// </summary>
        public ushort HighPressure { get; set; }

        /// <summary>
        /// 低压压力 (KPA)
        /// </summary>
        public ushort LowPressure { get; set; }

        /// <summary>
        /// 氢气阀状态 (0:关闭, 1:打开)
        /// </summary>
        public byte HydrogenValveState { get; set; }

        /// <summary>
        /// 排水阀状态 (0:关闭, 1:打开)
        /// </summary>
        public byte DrainValveState { get; set; }

        /// <summary>
        /// DCDC输入电压 (0.1V)
        /// </summary>
        public ushort DCDCInputVoltage { get; set; }

        /// <summary>
        /// DCDC输入电流 (0.1A)
        /// </summary>
        public ushort DCDCInputCurrent { get; set; }

        /// <summary>
        /// DCDC输出电压 (0.1V)
        /// </summary>
        public ushort DCDCOutputVoltage { get; set; }

        /// <summary>
        /// DCDC输出电流 (0.1A)
        /// </summary>
        public ushort DCDCOutputCurrent { get; set; }

        /// <summary>
        /// 风扇转速 (RPM)
        /// </summary>
        public ushort FanSpeed { get; set; }

        /// <summary>
        /// 电堆温度 (PT1000，0.1℃，有符号，下位机下限 -40℃)
        /// </summary>
        public short StackTemperature { get; set; }

        /// <summary>
        /// FCU控制器温度 (0.1℃，无符号)
        /// </summary>
        public ushort FCUTemperature { get; set; }

        /// <summary>
        /// 累计运行时间 (秒)
        /// </summary>
        public uint RunTime { get; set; }

        /// <summary>
        /// 故障码
        /// </summary>
        public byte FaultCode { get; set; }
    }

    /// <summary>
    /// 压力参数结构
    /// 对应压力参数数据域18字节（含参数ID）
    /// </summary>
    public class PressureParams
    {
        /// <summary>
        /// 高压最小值 (KPA)
        /// </summary>
        public ushort HighPressureMin { get; set; }

        /// <summary>
        /// 高压红警值 (KPA)
        /// </summary>
        public ushort HighPressureRed { get; set; }

        /// <summary>
        /// 高压黄警值 (KPA)
        /// </summary>
        public ushort HighPressureYellow { get; set; }

        /// <summary>
        /// 高压上限告警值 (KPA)，超过时触发 CRITICAL 停机
        /// </summary>
        public ushort HighPressureMax { get; set; }

        /// <summary>
        /// 低压下限值 (KPA)
        /// </summary>
        public ushort LowPressureMin { get; set; }

        /// <summary>
        /// 低压上限值 (KPA)
        /// </summary>
        public ushort LowPressureMax { get; set; }

        /// <summary>
        /// 高压量程 (KPA) - V4.2新增
        /// </summary>
        public ushort HighPressureRange { get; set; }

        /// <summary>
        /// 低压量程 (KPA) - V4.2新增
        /// </summary>
        public ushort LowPressureRange { get; set; }
    }

    /// <summary>
    /// 阀门参数结构
    /// </summary>
    public class ValveParams
    {
        /// <summary>
        /// 开机第一次时长 (0.1s)
        /// </summary>
        public ushort FirstOpenTime { get; set; }

        /// <summary>
        /// 开机第二次时长 (0.1s)
        /// </summary>
        public ushort SecondOpenTime { get; set; }

        /// <summary>
        /// 正常周期间隔 (0.1s)
        /// </summary>
        public ushort NormalInterval { get; set; }

        /// <summary>
        /// 正常打开时长 (0.1s)
        /// </summary>
        public ushort NormalOpenTime { get; set; }

        /// <summary>
        /// 关机打开时长 (0.1s)
        /// </summary>
        public ushort ShutdownOpenTime { get; set; }
    }

    /// <summary>
    /// DCDC参数结构
    /// </summary>
    public class DCDCParams
    {
        /// <summary>
        /// 最低工作电压 (0.1V)
        /// </summary>
        public ushort MinVoltage { get; set; }

        /// <summary>
        /// 放电截至电压 (0.1V)
        /// </summary>
        public ushort StopVoltage { get; set; }
    }

    /// <summary>
    /// 风扇温度参数结构
    /// </summary>
    public class FanTempParams
    {
        /// <summary>
        /// 低速占空比 (%)
        /// </summary>
        public byte LowSpeedDuty { get; set; }

        /// <summary>
        /// 温度下限 (0.1℃，无符号，对应固件 uint16_t temp_lower_limit)
        /// </summary>
        public ushort TempMin { get; set; }

        /// <summary>
        /// 温度上限 (0.1℃，无符号，对应固件 uint16_t temp_upper_limit)
        /// </summary>
        public ushort TempMax { get; set; }

        /// <summary>
        /// 全速运行时长 (0.1s)
        /// </summary>
        public ushort FullSpeedTime { get; set; }

        /// <summary>
        /// 低温检测时长 (0.1s)
        /// </summary>
        public ushort LowTempCheckTime { get; set; }

        /// <summary>
        /// 风扇最低转速 (RPM)，0 表示不启用堵转检测
        /// </summary>
        public ushort FanMinRpm { get; set; }
    }

    /// <summary>
    /// BMS/SOC 参数结构（参数类型ID 0x0701，参数数据5字节）。
    /// </summary>
    public class SocParams
    {
        /// <summary>
        /// 被动自启阈值: SOC 低于该值被动自动开机, 高于该值拒绝开机 (%)。
        /// 须小于 StopHigh 构成启动滞回 (固件 fcu_protocol_set_soc_params 校验)。
        /// </summary>
        public byte StartMax { get; set; }

        /// <summary>
        /// 运行中 SOC 大于该值时自动停机 (%)。
        /// </summary>
        public byte StopHigh { get; set; }

        /// <summary>
        /// 关机充电下限——固件当前版本中为保留字段, 仅存储回读, 不参与控制 (%)。
        /// </summary>
        public byte StopMin { get; set; }

        /// <summary>
        /// FCU 标称电池容量 (Ah)。
        /// </summary>
        public ushort BatteryCapacity { get; set; }
    }

    /// <summary>
    /// 工作流程参数 (0x0E/0x0601, 数据域12字节, 大端)
    /// 与固件 fcu_flow_params_t 一一对应 [fcu_protocol.h:231-238]
    /// 创建日期: 2026-08-06
    /// </summary>
    public class FlowParams
    {
        /// <summary>
        /// 关机purge进氢第一次开时长 (0.1s), 默认10(1.0s) [需求4.3]
        /// </summary>
        public ushort PurgeH2Open1 { get; set; }

        /// <summary>
        /// 关机purge进氢关断时长 (0.1s), 默认10(1.0s)
        /// </summary>
        public ushort PurgeH2Close { get; set; }

        /// <summary>
        /// 关机purge进氢第二次开时长 (0.1s), 默认10(1.0s)
        /// </summary>
        public ushort PurgeH2Open2 { get; set; }

        /// <summary>
        /// 关机排水保持时长 (0.1s), 默认30(3.0s) [需求4.2]
        /// </summary>
        public ushort PurgeDrainHold { get; set; }

        /// <summary>
        /// 排水模式: 0=定时(默认), 1=低压反馈 [需求3.4]
        /// 固件校验: >1 拒绝
        /// </summary>
        public byte DrainMode { get; set; }

        /// <summary>
        /// 低压反馈排水阈值 (kPa), 默认55 [需求3.4]
        /// 固件校验: ==0 拒绝
        /// </summary>
        public ushort LowNormalRef { get; set; }

        /// <summary>
        /// 进拉载风扇占空比 (%), 默认30 [需求3.3]
        /// 固件校验: >100 拒绝
        /// </summary>
        public byte LoadDuty { get; set; }
    }

    /// <summary>
    /// 报警信息结构
    /// 对应协议6.1节
    /// </summary>
    public class AlarmData
    {
        /// <summary>
        /// 报警类型代码
        /// </summary>
        public ushort AlarmType { get; set; }

        /// <summary>
        /// 报警等级 (0x01:INFO, 0x02:WARNING, 0x03:ERROR, 0x04:CRITICAL)
        /// </summary>
        public byte AlarmLevel { get; set; }

        /// <summary>
        /// 报警数值
        /// </summary>
        public ushort AlarmValue { get; set; }

        /// <summary>
        /// 获取报警类型名称
        /// </summary>
        public string GetAlarmTypeName()
        {
            return AlarmType switch
            {
                0x0001 => "高压压力低",
                0x0002 => "温度过高",
                0x0003 => "低压压力异常",
                0x0004 => "风扇异常",
                0x0005 => "电压异常",
                0x0006 => "DCDC异常",
                0x0007 => "阀门异常",
                0x0008 => "BMS故障",
                0x0009 => "FCU控制器过热",
                0x000A => "高压压力高",
                0x000B => "充电异常",
                0x000C => "低压超限泄压警示",
                _ => "未知报警"
            };
        }

        /// <summary>
        /// 获取报警数值描述。
        /// AlarmValue 的单位随报警类型而变，需分别换算，下位机 fcu_control.c/fcu_fan.c 的实际口径：
        ///   0x0001/0x0003/0x000A 压力类 → KPA 原值       (fcu_control.c:269,392,414,427,439)
        ///   0x0002        温度过高 → 温度×10 (0.1℃)     (fcu_control.c:457)
        ///   0x0006        DCDC异常 → 电压×10 (0.1V)     (fcu_control.c:242,501)
        ///   0x0008        BMS故障  → reg139 故障位图     (fcu_control.c:136)
        ///   0x0004        风扇异常 → 当前转速 RPM        (fcu_fan.c 堵转/缺相检测, 协议V5.1)
        ///   0x000B        充电异常 → SOC % (1%单位)      (fcu_control.c 充电停滞告警)
        ///   0x000C        泄压警示 → KPA 原值            (fcu_control.c low_pressure_task, >80KPA开阀WARNING, 协议名「低压超限泄压警示」)
        /// 0x0005(电压异常)/0x0009(FCU过热) 固件当前无触发点，按同类语义预置换算。
        /// 注：固件行号随提交漂移，以 FCU_ALARM_* 枚举 grep 定位为准。
        /// </summary>
        public string GetAlarmValueDescription()
        {
            return AlarmType switch
            {
                // 压力类：固件直接送 KPA 原值，不缩放（0x000A 高压过压、0x000C 泄压警示同为 KPA 原值）
                0x0001 or 0x0003 or 0x000A or 0x000C => $"{AlarmValue} KPA",

                // 温度类：固件送 温度×10，按有符号还原（下位机允许负温）
                0x0002 or 0x0009 => $"{(short)AlarmValue / 10.0:F1} ℃",

                // 电压类：固件送 电压×10
                0x0005 or 0x0006 => $"{AlarmValue / 10.0:F1} V",

                // BMS故障：AlarmValue 为 BMS 故障位图(reg139), 按附表3解码 [上位机优化计划 h-t6]
                0x0008 => BmsBitDecoder.DecodeFault(AlarmValue),

                // 风扇异常：堵转/缺相检测上报的当前转速（协议V5.1, WARNING级不停机）
                0x0004 => $"{AlarmValue} RPM",

                // 充电异常：充电三条件满足但 SOC 持续不涨，数值为当前 SOC(%)
                0x000B => $"SOC {AlarmValue} %",

                // 0x0007 阀门异常：固件无触发点，单位未定义，原值输出
                _ => AlarmValue.ToString()
            };
        }

        /// <summary>
        /// 获取报警等级名称
        /// </summary>
        public string GetAlarmLevelName()
        {
            return AlarmLevel switch
            {
                0x01 => "信息",
                0x02 => "警告",
                0x03 => "错误",
                0x04 => "严重",
                _ => "未知"
            };
        }
    }

    /// <summary>
    /// 状态变更数据结构
    /// 对应协议6.2节
    /// </summary>
    public class StateChangeData
    {
        /// <summary>
        /// 状态类型代码 (0x0101:电堆工作状态, 0x0102:风扇运行状态, 0x0103:DCDC工作状态, 0x0104:阀门状态, 0x0105:低压泄压保护)
        /// </summary>
        public ushort StateType { get; set; }

        /// <summary>
        /// 旧状态
        /// </summary>
        public byte OldState { get; set; }

        /// <summary>
        /// 新状态
        /// </summary>
        public byte NewState { get; set; }

        /// <summary>
        /// 获取状态类型名称
        /// </summary>
        public string GetStateTypeName()
        {
            return StateType switch
            {
                0x0101 => "电堆工作状态",
                0x0102 => "风扇运行状态",
                0x0103 => "DCDC工作状态",
                0x0104 => "阀门状态",
                0x0105 => "低压泄压保护",
                _ => "未知状态"
            };
        }
    }

    /// <summary>
    /// 通用参数结构
    /// </summary>
    public class GeneralParams
    {
        /// <summary>
        /// 扫描周期 (0.1s)
        /// </summary>
        public ushort ScanPeriod { get; set; }
    }

    /// <summary>
    /// 拉载时序参数结构（参数类型ID 0x0501）
    /// </summary>
    public class LoadSequenceParams
    {
        /// <summary>
        /// 时序总数
        /// </summary>
        public byte SequenceCount { get; set; }

        /// <summary>
        /// 各时序电流值 (0.1A)
        /// </summary>
        public ushort[] Currents { get; set; } = new ushort[15];

        /// <summary>
        /// 各时序持续时间 (0.01s)
        /// </summary>
        public ushort[] Times { get; set; } = new ushort[15];
    }

    /// <summary>
    /// 拉载控制参数结构（参数类型ID 0x0502）
    /// </summary>
    public class LoadControlParams
    {
        /// <summary>
        /// 峰值电流 (0.1A)
        /// </summary>
        public ushort PeakCurrent { get; set; }

        /// <summary>
        /// 锁定系数 (0.001)
        /// </summary>
        public ushort LockFactor { get; set; }

        /// <summary>
        /// 锁定标志 (0:不锁定, 1:锁定)
        /// </summary>
        public byte LockFlag { get; set; }

        /// <summary>
        /// 最低电压 (0.1V)
        /// </summary>
        public ushort MinVoltage { get; set; }

        /// <summary>
        /// 电流步长 (0.1A)
        /// </summary>
        public ushort CurrentStep { get; set; }
    }

    /// <summary>
    /// 拉载启动参数结构（参数类型ID 0x0503）
    /// </summary>
    public class LoadStartParams
    {
        /// <summary>
        /// 首次拉载次数
        /// </summary>
        public byte FirstCount { get; set; }

        /// <summary>
        /// 首次拉载间隔 (分钟)
        /// </summary>
        public ushort FirstInterval { get; set; }

        /// <summary>
        /// 正常拉载间隔 (分钟)
        /// </summary>
        public ushort NormalInterval { get; set; }
    }

    /// <summary>
    /// 拉载状态上报数据结构（功能码0xF2，14字节）
    /// </summary>
    public class LoadStatusData
    {
        /// <summary>
        /// 当前时序编号
        /// </summary>
        public byte CurrentSequence { get; set; }

        /// <summary>
        /// 当前电流 (0.1A)
        /// </summary>
        public ushort CurrentCurrent { get; set; }

        /// <summary>
        /// 目标电流 (0.1A)
        /// </summary>
        public ushort TargetCurrent { get; set; }

        /// <summary>
        /// 当前电压 (0.1V)
        /// </summary>
        public ushort CurrentVoltage { get; set; }

        /// <summary>
        /// 当前功率 (0.1W)
        /// </summary>
        public ushort CurrentPower { get; set; }

        /// <summary>
        /// 最大功率 (0.1W)
        /// </summary>
        public ushort MaxPower { get; set; }

        /// <summary>
        /// 锁定电流 (0.1A)
        /// </summary>
        public ushort LockCurrent { get; set; }

        /// <summary>
        /// 拉载状态码
        /// </summary>
        public byte LoadState { get; set; }

        /// <summary>
        /// 获取拉载状态名称
        /// </summary>
        public string GetLoadStateName()
        {
            return LoadState switch
            {
                0x00 => "未开始",
                0x01 => "进行中",
                0x02 => "完成",
                0x03 => "暂停",
                0x04 => "异常停止",
                0x05 => "锁定成功",
                0x06 => "锁定失败",
                _ => $"未知状态(0x{LoadState:X2})"
            };
        }
    }
}
