using System;
using System.Text;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// 响应数据解析器
    /// </summary>
    public static class ResponseParser
    {
        /// <summary>
        /// 解析设备序列号（功能码0x81）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>设备序列号字符串</returns>
        public static string ParseSerialNumber(byte[] frame)
        {
            try
            {
                // 数据域：12字节ASCII字符串
                byte dataLen = frame[4];
                if (dataLen != 12)
                    return "--";

                byte[] serialData = new byte[12];
                Array.Copy(frame, 5, serialData, 0, 12);

                return Encoding.ASCII.GetString(serialData).TrimEnd('\0');
            }
            catch
            {
                return "--";
            }
        }

        /// <summary>
        /// 解析电堆信息（功能码0x87）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>电堆信息数据</returns>
        public static StackInfoData ParseStackInfo(byte[] frame)
        {
            var data = new StackInfoData();

            try
            {
                byte dataLen = frame[4];
                if (dataLen != 26)
                    return data;

                int offset = 5; // 数据域起始位置

                // 字节0: 电堆工作状态
                data.WorkState = frame[offset];

                // 字节1-2: 高压压力 (大端序)
                data.HighPressure = (ushort)((frame[offset + 1] << 8) | frame[offset + 2]);

                // 字节3-4: 低压压力
                data.LowPressure = (ushort)((frame[offset + 3] << 8) | frame[offset + 4]);

                // 字节5: 氢气阀状态
                data.HydrogenValveState = frame[offset + 5];

                // 字节6: 排水阀状态
                data.DrainValveState = frame[offset + 6];

                // 字节7-8: DCDC输入电压
                data.DCDCInputVoltage = (ushort)((frame[offset + 7] << 8) | frame[offset + 8]);

                // 字节9-10: DCDC输入电流
                data.DCDCInputCurrent = (ushort)((frame[offset + 9] << 8) | frame[offset + 10]);

                // 字节11-12: DCDC输出电压
                data.DCDCOutputVoltage = (ushort)((frame[offset + 11] << 8) | frame[offset + 12]);

                // 字节13-14: DCDC输出电流
                data.DCDCOutputCurrent = (ushort)((frame[offset + 13] << 8) | frame[offset + 14]);

                // 字节15-16: 风扇转速
                data.FanSpeed = (ushort)((frame[offset + 15] << 8) | frame[offset + 16]);

                // 字节17-18: 电堆温度
                data.StackTemperature = (ushort)((frame[offset + 17] << 8) | frame[offset + 18]);

                // 字节19-20: FCU控制器温度
                data.FCUTemperature = (ushort)((frame[offset + 19] << 8) | frame[offset + 20]);

                // 字节21-24: 累计运行时间 (大端序, 4字节)
                data.RunTime = (uint)((frame[offset + 21] << 24) | (frame[offset + 22] << 16) |
                                      (frame[offset + 23] << 8) | frame[offset + 24]);

                // 字节25: 故障码
                data.FaultCode = frame[offset + 25];
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析压力参数（参数类型ID 0x0101）
        /// V4.2协议数据域16字节
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>压力参数</returns>
        public static PressureParams ParsePressureParams(byte[] frame)
        {
            var data = new PressureParams();

            try
            {
                // 0x8A响应帧数据域：参数类型ID(2) + 参数数据(N)
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                // 高压最小值
                data.HighPressureMin = (ushort)((frame[offset] << 8) | frame[offset + 1]);

                // 高压红警值
                data.HighPressureRed = (ushort)((frame[offset + 2] << 8) | frame[offset + 3]);

                // 高压黄警值
                data.HighPressureYellow = (ushort)((frame[offset + 4] << 8) | frame[offset + 5]);

                // 低压下限值
                data.LowPressureMin = (ushort)((frame[offset + 6] << 8) | frame[offset + 7]);

                // 低压上限值
                data.LowPressureMax = (ushort)((frame[offset + 8] << 8) | frame[offset + 9]);

                // 高压量程 (V4.2新增)
                data.HighPressureRange = (ushort)((frame[offset + 10] << 8) | frame[offset + 11]);

                // 低压量程 (V4.2新增)
                data.LowPressureRange = (ushort)((frame[offset + 12] << 8) | frame[offset + 13]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析阀门参数（参数类型ID 0x0201）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>阀门参数</returns>
        public static ValveParams ParseValveParams(byte[] frame)
        {
            var data = new ValveParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.FirstOpenTime = (ushort)((frame[offset] << 8) | frame[offset + 1]);
                data.SecondOpenTime = (ushort)((frame[offset + 2] << 8) | frame[offset + 3]);
                data.NormalInterval = (ushort)((frame[offset + 4] << 8) | frame[offset + 5]);
                data.NormalOpenTime = (ushort)((frame[offset + 6] << 8) | frame[offset + 7]);
                data.ShutdownOpenTime = (ushort)((frame[offset + 8] << 8) | frame[offset + 9]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析DCDC参数（参数类型ID 0x0301）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>DCDC参数</returns>
        public static DCDCParams ParseDCDCParams(byte[] frame)
        {
            var data = new DCDCParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.MinVoltage = (ushort)((frame[offset] << 8) | frame[offset + 1]);
                data.StopVoltage = (ushort)((frame[offset + 2] << 8) | frame[offset + 3]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析风扇温度参数（参数类型ID 0x0401）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>风扇温度参数</returns>
        public static FanTempParams ParseFanTempParams(byte[] frame)
        {
            var data = new FanTempParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.LowSpeedDuty = frame[offset];
                data.TempMin = (ushort)((frame[offset + 1] << 8) | frame[offset + 2]);
                data.TempMax = (ushort)((frame[offset + 3] << 8) | frame[offset + 4]);
                data.FullSpeedTime = (ushort)((frame[offset + 5] << 8) | frame[offset + 6]);
                data.LowTempCheckTime = (ushort)((frame[offset + 7] << 8) | frame[offset + 8]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析通用参数（参数类型ID 0x0901）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>通用参数</returns>
        public static GeneralParams ParseGeneralParams(byte[] frame)
        {
            var data = new GeneralParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.ScanPeriod = (ushort)((frame[offset] << 8) | frame[offset + 1]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析报警信息（功能码0xF0）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>报警信息</returns>
        public static AlarmData ParseAlarmInfo(byte[] frame)
        {
            var data = new AlarmData();

            try
            {
                byte dataLen = frame[4];
                if (dataLen != 5)
                    return data;

                int offset = 5; // 数据域起始位置

                // 字节0-1: 报警类型
                data.AlarmType = (ushort)((frame[offset] << 8) | frame[offset + 1]);

                // 字节2: 报警等级
                data.AlarmLevel = frame[offset + 2];

                // 字节3-4: 报警数值
                data.AlarmValue = (ushort)((frame[offset + 3] << 8) | frame[offset + 4]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析状态变更（功能码0xF1）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>状态变更数据</returns>
        public static StateChangeData ParseStateChange(byte[] frame)
        {
            var data = new StateChangeData();

            try
            {
                byte dataLen = frame[4];
                if (dataLen != 4)
                    return data;

                int offset = 5; // 数据域起始位置

                // 字节0-1: 状态类型
                data.StateType = (ushort)((frame[offset] << 8) | frame[offset + 1]);

                // 字节2: 旧状态
                data.OldState = frame[offset + 2];

                // 字节3: 新状态
                data.NewState = frame[offset + 3];
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析拉载时序参数（参数类型ID 0x0501，0x8A响应）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>拉载时序参数</returns>
        public static LoadSequenceParams ParseLoadSequenceParams(byte[] frame)
        {
            var data = new LoadSequenceParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.SequenceCount = frame[offset];
                int count = Math.Min((int)data.SequenceCount, 15);

                for (int i = 0; i < count; i++)
                {
                    int seqOffset = offset + 1 + i * 4;
                    data.Currents[i] = (ushort)((frame[seqOffset] << 8) | frame[seqOffset + 1]);
                    data.Times[i] = (ushort)((frame[seqOffset + 2] << 8) | frame[seqOffset + 3]);
                }
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析拉载控制参数（参数类型ID 0x0502，0x8A响应）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>拉载控制参数</returns>
        public static LoadControlParams ParseLoadControlParams(byte[] frame)
        {
            var data = new LoadControlParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.PeakCurrent = (ushort)((frame[offset] << 8) | frame[offset + 1]);
                data.LockFactor = (ushort)((frame[offset + 2] << 8) | frame[offset + 3]);
                data.LockFlag = frame[offset + 4];
                data.MinVoltage = (ushort)((frame[offset + 5] << 8) | frame[offset + 6]);
                data.CurrentStep = (ushort)((frame[offset + 7] << 8) | frame[offset + 8]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析拉载启动参数（参数类型ID 0x0503，0x8A响应）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>拉载启动参数</returns>
        public static LoadStartParams ParseLoadStartParams(byte[] frame)
        {
            var data = new LoadStartParams();

            try
            {
                // offset=7: 跳过帧头(2)+地址(1)+功能码(1)+长度(1)+参数类型ID(2)
                int offset = 7;

                data.FirstCount = frame[offset];
                data.FirstInterval = (ushort)((frame[offset + 1] << 8) | frame[offset + 2]);
                data.NormalInterval = (ushort)((frame[offset + 3] << 8) | frame[offset + 4]);
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析拉载状态上报（功能码0xF2，14字节数据域）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>拉载状态数据</returns>
        public static LoadStatusData ParseLoadStatusReport(byte[] frame)
        {
            var data = new LoadStatusData();

            try
            {
                byte dataLen = frame[4];
                if (dataLen != ProtocolConstants.LOAD_STATUS_DATA_LENGTH)
                    return data;

                int offset = 5; // 数据域起始位置

                data.CurrentSequence = frame[offset];
                data.CurrentCurrent = (ushort)((frame[offset + 1] << 8) | frame[offset + 2]);
                data.TargetCurrent = (ushort)((frame[offset + 3] << 8) | frame[offset + 4]);
                data.CurrentVoltage = (ushort)((frame[offset + 5] << 8) | frame[offset + 6]);
                data.CurrentPower = (ushort)((frame[offset + 7] << 8) | frame[offset + 8]);
                data.MaxPower = (ushort)((frame[offset + 9] << 8) | frame[offset + 10]);
                data.LockCurrent = (ushort)((frame[offset + 11] << 8) | frame[offset + 12]);
                data.LoadState = frame[offset + 13];
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 解析参数设置响应（功能码0x82）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>结果码 (0x00:成功, 其他:错误码)</returns>
        public static byte ParseSetParamResponse(byte[] frame)
        {
            try
            {
                byte dataLen = frame[4];
                if (dataLen != 1)
                    return 0xFF;

                return frame[5]; // 返回结果码
            }
            catch
            {
                return 0xFF;
            }
        }

        /// <summary>
        /// 解析启动/关闭响应（功能码0x88/0x89）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>结果码</returns>
        public static byte ParseControlResponse(byte[] frame)
        {
            try
            {
                byte dataLen = frame[4];
                if (dataLen != 1)
                    return 0xFF;

                return frame[5]; // 返回结果码
            }
            catch
            {
                return 0xFF;
            }
        }

        /// <summary>
        /// 获取功能码
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>功能码</returns>
        public static byte GetFunctionCode(byte[] frame)
        {
            if (frame.Length >= 4)
                return frame[3];
            return 0;
        }

        /// <summary>
        /// 获取参数类型ID（用于0x8A响应）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>参数类型ID</returns>
        public static ushort GetParameterTypeID(byte[] frame)
        {
            try
            {
                if (frame.Length >= 7)
                    return (ushort)((frame[5] << 8) | frame[6]);
            }
            catch
            {
                // 解析异常
            }

            return 0;
        }

        /// <summary>
        /// 获取数据域（从完整帧中提取数据域）
        /// </summary>
        /// <param name="frame">完整帧数据</param>
        /// <returns>数据域字节数组</returns>
        public static byte[] GetDataField(byte[] frame)
        {
            try
            {
                if (frame.Length < 5)
                    return new byte[0];

                byte dataLen = frame[4];
                if (dataLen == 0 || frame.Length < (7 + dataLen))
                    return new byte[0];

                byte[] dataField = new byte[dataLen];
                Array.Copy(frame, 5, dataField, 0, dataLen);
                return dataField;
            }
            catch
            {
                return new byte[0];
            }
        }
    }
}
