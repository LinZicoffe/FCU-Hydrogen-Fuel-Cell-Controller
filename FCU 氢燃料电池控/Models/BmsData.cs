using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// BMS 数据模型 (V1.28-S Modbus RTU, 03H 读 reg128~152)
    /// 创建日期: 2026-08-06
    /// [BMS协议 1.28-S §3.1] 寄存器原始值 + 工程值便捷属性
    /// </summary>
    public class BmsData
    {
        /// <summary>BMS 在线标志 (连续3次超时置离线, 仿固件宽松策略)</summary>
        public bool Online { get; set; }

        /// <summary>最近一次成功解析时间</summary>
        public DateTime LastUpdate { get; set; }

        /// <summary>reg128 电流原始值 (S16, 0.01A, 充电正放电负)</summary>
        public short CurrentRaw { get; set; }

        /// <summary>reg129 电池组电压原始值 (U16, 0.01V)</summary>
        public ushort PackVoltageRaw { get; set; }

        /// <summary>reg130 SOC (%)</summary>
        public ushort Soc { get; set; }

        /// <summary>reg131 SOH (%)</summary>
        public ushort Soh { get; set; }

        /// <summary>reg132 剩余容量原始值 (0.01AH)</summary>
        public ushort RemainCapacityRaw { get; set; }

        /// <summary>reg133 满充容量原始值 (0.01AH)</summary>
        public ushort FullCapacityRaw { get; set; }

        /// <summary>reg134 额定容量原始值 (0.01AH)</summary>
        public ushort RatedCapacityRaw { get; set; }

        /// <summary>reg135 循环次数 (Cyc.)</summary>
        public ushort CycleCount { get; set; }

        /// <summary>reg137 告警状态位图 (附表1)</summary>
        public ushort AlarmStatus { get; set; }

        /// <summary>reg138 保护状态位图 (附表2)</summary>
        public ushort ProtectStatus { get; set; }

        /// <summary>reg139 故障状态位图 (附表3)</summary>
        public ushort FaultStatus { get; set; }

        /// <summary>reg140 系统状态位图 (附表4, BIT1=充电MOS, BIT2=放电MOS)</summary>
        public ushort SystemStatus { get; set; }

        /// <summary>reg141 功能开关状态位图 (附表5)</summary>
        public ushort SwitchStatus { get; set; }

        /// <summary>reg145 电池组串数</summary>
        public ushort SeriesCells { get; set; }

        /// <summary>reg146 单体电压最大值 (mV)</summary>
        public ushort CellVoltMaxMv { get; set; }

        /// <summary>reg147 单体电压最小值 (mV)</summary>
        public ushort CellVoltMinMv { get; set; }

        /// <summary>reg148 电芯温度个数</summary>
        public ushort CellTempCount { get; set; }

        /// <summary>reg149 电芯温度最大值原始值 (S16, 0.1℃)</summary>
        public short CellTempMaxRaw { get; set; }

        /// <summary>reg150 电芯温度最小值原始值 (S16, 0.1℃)</summary>
        public short CellTempMinRaw { get; set; }

        /// <summary>reg151 功率管温度原始值 (S16, 0.1℃, 0x8000=未监测)</summary>
        public short MosTempRaw { get; set; }

        /// <summary>reg152 环境温度原始值 (S16, 0.1℃, 0x8000=未监测)</summary>
        public short EnvTempRaw { get; set; }

        #region 工程值便捷属性

        /// <summary>电流 (A, 充电正放电负)</summary>
        public double Current => CurrentRaw * 0.01;

        /// <summary>电池组电压 (V)</summary>
        public double PackVoltage => PackVoltageRaw * 0.01;

        /// <summary>剩余容量 (AH)</summary>
        public double RemainCapacity => RemainCapacityRaw * 0.01;

        /// <summary>满充容量 (AH)</summary>
        public double FullCapacity => FullCapacityRaw * 0.01;

        /// <summary>额定容量 (AH)</summary>
        public double RatedCapacity => RatedCapacityRaw * 0.01;

        /// <summary>单体电压最大值 (V)</summary>
        public double CellVoltMax => CellVoltMaxMv / 1000.0;

        /// <summary>单体电压最小值 (V)</summary>
        public double CellVoltMin => CellVoltMinMv / 1000.0;

        /// <summary>电芯温度最大值 (℃)</summary>
        public double CellTempMax => CellTempMaxRaw * 0.1;

        /// <summary>电芯温度最小值 (℃)</summary>
        public double CellTempMin => CellTempMinRaw * 0.1;

        /// <summary>功率管温度是否已监测</summary>
        public bool MosTempValid => MosTempRaw != unchecked((short)0x8000);

        /// <summary>环境温度是否已监测</summary>
        public bool EnvTempValid => EnvTempRaw != unchecked((short)0x8000);

        /// <summary>充电MOS是否开启 [BMS协议 附表4 BIT1]</summary>
        public bool ChargeMosOn => (SystemStatus & 0x0002) != 0;

        /// <summary>放电MOS是否开启 [BMS协议 附表4 BIT2]</summary>
        public bool DischargeMosOn => (SystemStatus & 0x0004) != 0;

        #endregion
    }
}
