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
        private const int ReadConfigParameterDataOffset = 8;

        private static bool TryGetReadConfigParameterData(
            byte[] frame,
            ushort expectedParameterId,
            out int dataOffset,
            out int parameterDataLength)
        {
            dataOffset = ReadConfigParameterDataOffset;
            parameterDataLength = 0;

            if (frame == null || frame.Length < ProtocolConstants.MIN_FRAME_LENGTH)
                return false;

            byte dataLength = frame[4];
            if (frame[0] != ProtocolConstants.FRAME_HEADER1 ||
                frame[1] != ProtocolConstants.FRAME_HEADER2 ||
                frame[3] != ProtocolConstants.RESP_READ_CONFIG ||
                dataLength < 3 ||
                frame.Length != ProtocolConstants.MIN_FRAME_LENGTH + dataLength)
            {
                return false;
            }

            ushort parameterId = (ushort)((frame[5] << 8) | frame[6]);
            if (parameterId != expectedParameterId)
                return false;

            parameterDataLength = dataLength - 3; // parameter ID (2) + sub-index (1)
            return true;
        }

        /// <summary>
        /// 0x8A 读配置响应中，各参数ID对应的参数数据域期望长度（不含参数ID与子索引）。
        /// 长度值与下位机 fcu_handle_read_config() 各分支的 data_len 一一对应。
        /// 0x0501 拉载时序为变长（时序数×4），以 -1 表示，另做整除校验。
        /// </summary>
        private static int GetExpectedParameterDataLength(ushort parameterId)
        {
            return parameterId switch
            {
                ProtocolConstants.PARAM_TYPE_PRESSURE => 14,
                ProtocolConstants.PARAM_TYPE_VALVE => 10,
                ProtocolConstants.PARAM_TYPE_DCDC => 4,
                ProtocolConstants.PARAM_TYPE_FAN_TEMP => 9,
                ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE => -1,   // 变长: 时序数 × 4
                ProtocolConstants.PARAM_TYPE_LOAD_CONTROL => 9,
                ProtocolConstants.PARAM_TYPE_LOAD_STARTUP => 5,
                ProtocolConstants.PARAM_TYPE_FLOW => 12,
                ProtocolConstants.PARAM_TYPE_GENERAL => 2,
                _ => -2                                             // 未知参数ID
            };
        }

        /// <summary>
        /// 校验 0x8A 读配置响应是否可被正常解析。
        /// 各 Parse*Params 方法在长度不符时会静默返回全 0 对象，若 UI 直接展示，
        /// 用户会看到一片 0 并被告知「读取成功」，无从判断是真实值还是解析失败。
        /// 因此 UI 侧必须先调用本方法，失败时给出明确原因而不是更新界面。
        /// </summary>
        /// <param name="frame">完整响应帧</param>
        /// <param name="parameterId">期望的参数类型ID</param>
        /// <param name="failReason">校验失败原因，成功时为空字符串</param>
        /// <returns>true 表示可安全解析</returns>
        public static bool TryValidateReadConfigResponse(byte[] frame, ushort parameterId,
            out string failReason)
        {
            failReason = string.Empty;

            int expected = GetExpectedParameterDataLength(parameterId);
            if (expected == -2)
            {
                failReason = $"未知参数类型ID 0x{parameterId:X4}";
                return false;
            }

            if (!TryGetReadConfigParameterData(frame, parameterId,
                    out _, out int actualLength))
            {
                failReason = "帧头/响应码/参数ID或帧长不匹配";
                return false;
            }

            if (expected == -1)
            {
                // 拉载时序变长: 必须是 4 的整数倍且至少 1 条
                if (actualLength == 0 || actualLength % 4 != 0)
                {
                    failReason = $"拉载时序数据域长度 {actualLength} 不是 4 的整数倍";
                    return false;
                }
                return true;
            }

            if (actualLength != expected)
            {
                failReason = $"数据域长度 {actualLength} 与期望值 {expected} 不符"
                           + "（可能是下位机协议版本与上位机不一致）";
                return false;
            }

            return true;
        }

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

                // 字节17-18: 电堆温度 (有符号, 0.1℃)
                // 下位机 fcu_sensor.c 允许温度低至 -40℃，编码时按二进制补码塞进 uint16，
                // 因此必须按 short 解析，否则 -40.0℃ 会被读成 65136 → 显示 6513.6℃
                data.StackTemperature = (short)((frame[offset + 17] << 8) | frame[offset + 18]);

                // 字节19-20: FCU控制器温度 (有符号, 0.1℃)
                data.FCUTemperature = (short)((frame[offset + 19] << 8) | frame[offset + 20]);

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_PRESSURE,
                    out int offset, out int parameterDataLength) || parameterDataLength != 14)
                    return data;

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_VALVE,
                    out int offset, out int parameterDataLength) || parameterDataLength != 10)
                    return data;

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_DCDC,
                    out int offset, out int parameterDataLength) || parameterDataLength != 4)
                    return data;

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_FAN_TEMP,
                    out int offset, out int parameterDataLength) || parameterDataLength != 9)
                    return data;

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
        /// 解析工作流程参数 (0x8A响应, 参数类型0x0601, 数据域12字节大端)
        /// 与固件 fcu_flow_params_t 一一对应 [fcu_protocol.c read_config 0x0601分支]
        /// 创建日期: 2026-08-06
        /// </summary>
        /// <param name="frame">完整响应帧</param>
        /// <returns>工作流程参数</returns>
        public static FlowParams ParseFlowParams(byte[] frame)
        {
            var data = new FlowParams();

            try
            {
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_FLOW,
                    out int offset, out int parameterDataLength) || parameterDataLength != 12)
                    return data;

                data.PurgeH2Open1 = (ushort)((frame[offset] << 8) | frame[offset + 1]);
                data.PurgeH2Close = (ushort)((frame[offset + 2] << 8) | frame[offset + 3]);
                data.PurgeH2Open2 = (ushort)((frame[offset + 4] << 8) | frame[offset + 5]);
                data.PurgeDrainHold = (ushort)((frame[offset + 6] << 8) | frame[offset + 7]);
                data.DrainMode = frame[offset + 8];
                data.LowNormalRef = (ushort)((frame[offset + 9] << 8) | frame[offset + 10]);
                data.LoadDuty = frame[offset + 11];
            }
            catch
            {
                // 解析异常时返回默认值
            }

            return data;
        }

        /// <summary>
        /// 构建工作流程参数数据域 (0x0E, 12字节大端, 不含参数类型ID)
        /// [fcu_protocol.c fcu_handle_set_flow_params 解析顺序]
        /// </summary>
        public static byte[] BuildFlowParamsData(FlowParams p)
        {
            return new byte[]
            {
                (byte)(p.PurgeH2Open1 >> 8), (byte)(p.PurgeH2Open1 & 0xFF),
                (byte)(p.PurgeH2Close >> 8), (byte)(p.PurgeH2Close & 0xFF),
                (byte)(p.PurgeH2Open2 >> 8), (byte)(p.PurgeH2Open2 & 0xFF),
                (byte)(p.PurgeDrainHold >> 8), (byte)(p.PurgeDrainHold & 0xFF),
                p.DrainMode,
                (byte)(p.LowNormalRef >> 8), (byte)(p.LowNormalRef & 0xFF),
                p.LoadDuty
            };
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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_GENERAL,
                    out int offset, out int parameterDataLength) || parameterDataLength != 2)
                    return data;

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_LOAD_SEQUENCE,
                    out int offset, out int parameterDataLength) ||
                    parameterDataLength % 4 != 0)
                {
                    return data;
                }

                // The firmware response omits a count byte. Each sequence occupies four bytes,
                // so the count is derived from the V4.3 response data length.
                int count = Math.Min(parameterDataLength / 4, data.Currents.Length);
                data.SequenceCount = (byte)count;

                for (int i = 0; i < count; i++)
                {
                    int seqOffset = offset + i * 4;
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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_LOAD_CONTROL,
                    out int offset, out int parameterDataLength) || parameterDataLength != 9)
                    return data;

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
                if (!TryGetReadConfigParameterData(frame, ProtocolConstants.PARAM_TYPE_LOAD_STARTUP,
                    out int offset, out int parameterDataLength) || parameterDataLength != 5)
                    return data;

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
                if (frame.Length >= ProtocolConstants.MIN_FRAME_LENGTH &&
                    frame[3] == ProtocolConstants.RESP_READ_CONFIG &&
                    frame[4] >= 3 &&
                    frame.Length == ProtocolConstants.MIN_FRAME_LENGTH + frame[4])
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
