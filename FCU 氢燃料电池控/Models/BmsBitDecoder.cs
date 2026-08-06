using System.Collections.Generic;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// BMS 状态位图解码器 (V1.28-S 附表1/2/3/4)
    /// 创建日期: 2026-08-06
    /// 用途: reg137告警/reg138保护/reg139故障 位图 → 中文文本;
    ///       reg139 解码同时供 FCU 报警0x0008(BMS故障)的值描述使用 [上位机优化计划 h-t6]
    /// </summary>
    public static class BmsBitDecoder
    {
        /// <summary>[BMS协议 附表1] reg137 告警状态位定义</summary>
        private static readonly string[] AlarmBits =
        {
            "单体高压告警",       // BIT0
            "单体低压告警",       // BIT1
            "总体高压告警",       // BIT2
            "总体低压告警",       // BIT3
            "充电过流告警",       // BIT4
            "放电过流告警",       // BIT5
            "压差告警",           // BIT6
            "预留",               // BIT7
            "电芯充电高温告警",   // BIT8
            "电芯放电高温告警",   // BIT9
            "电芯充电低温告警",   // BIT10
            "电芯放电低温告警",   // BIT11
            "环境(PCB)高温告警",  // BIT12
            "环境(PCB)低温告警",  // BIT13
            "功率管高温告警",     // BIT14
            "电量告警"            // BIT15
        };

        /// <summary>[BMS协议 附表2] reg138 保护状态位定义</summary>
        private static readonly string[] ProtectBits =
        {
            "单体过压保护",       // BIT0
            "单体过放保护",       // BIT1
            "总体过压保护",       // BIT2
            "总体过放保护",       // BIT3
            "充电过流保护",       // BIT4
            "放电过流保护",       // BIT5
            "短路",               // BIT6
            "充电器高压",         // BIT7 (文档标注预留)
            "电芯充电高温保护",   // BIT8
            "电芯放电高温保护",   // BIT9
            "电芯充电低温保护",   // BIT10
            "电芯放电低温保护",   // BIT11
            "环境(PCB)高温保护",  // BIT12
            "环境(PCB)低温保护",  // BIT13
            "功率管高温保护",     // BIT14
            "满充保护"            // BIT15 (文档标注预留)
        };

        /// <summary>[BMS协议 附表3] reg139 故障状态位定义</summary>
        private static readonly string[] FaultBits =
        {
            "充电MOS故障",        // BIT0
            "放电MOS故障",        // BIT1
            "温度传感器(NTC)故障",// BIT2
            "压差保护",           // BIT3
            "电芯故障",           // BIT4
            "模拟前端采样故障",   // BIT5
            "预充故障",           // BIT6
            "限流故障",           // BIT7
            "BMS内部DC电源故障",  // BIT8
            "预留",               // BIT9
            "预留",               // BIT10
            "预留",               // BIT11
            "预留",               // BIT12
            "预留",               // BIT13
            "预留",               // BIT14
            "加热功能故障"        // BIT15
        };

        /// <summary>解码 reg137 告警状态</summary>
        public static string DecodeAlarm(ushort bits) => Decode(bits, AlarmBits, "无告警");

        /// <summary>解码 reg138 保护状态</summary>
        public static string DecodeProtect(ushort bits) => Decode(bits, ProtectBits, "无保护");

        /// <summary>解码 reg139 故障状态</summary>
        public static string DecodeFault(ushort bits) => Decode(bits, FaultBits, "无故障");

        private static string Decode(ushort bits, string[] names, string noneText)
        {
            if (bits == 0)
            {
                return noneText;
            }

            var active = new List<string>();
            for (int i = 0; i < 16; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    active.Add(names[i]);
                }
            }
            return string.Join(", ", active);
        }
    }
}
