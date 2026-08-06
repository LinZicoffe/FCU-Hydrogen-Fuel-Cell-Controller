using System;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Managers
{
    /// <summary>
    /// 参数管理器 - 负责参数的读取和设置操作
    /// 创建日期: 2026-02-03
    /// 用途: 封装参数操作逻辑，提供统一的参数管理接口
    /// </summary>
    public class ParameterManager
    {
        private readonly SerialPortManager _serialPortManager;
        private byte _deviceAddress = ProtocolConstants.DEFAULT_DEVICE_ADDRESS;

        #region 事件定义

        /// <summary>
        /// 参数操作状态事件
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// 参数操作错误事件
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        #endregion

        #region 属性

        /// <summary>
        /// 设备地址
        /// </summary>
        public byte DeviceAddress
        {
            get => _deviceAddress;
            set => _deviceAddress = value;
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="serialPortManager">串口管理器</param>
        public ParameterManager(SerialPortManager serialPortManager)
        {
            _serialPortManager = serialPortManager ?? throw new ArgumentNullException(nameof(serialPortManager));
        }

        #endregion

        #region 帧构建方法

        /// <summary>
        /// 构建通信帧
        /// </summary>
        /// <param name="address">设备地址</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="data">数据域</param>
        /// <returns>完整的通信帧</returns>
        private byte[] BuildFrame(byte address, byte functionCode, byte[] data)
        {
            int frameLength = ProtocolConstants.MIN_FRAME_LENGTH + data.Length;
            byte[] frame = new byte[frameLength];

            frame[0] = ProtocolConstants.FRAME_HEADER1;
            frame[1] = ProtocolConstants.FRAME_HEADER2;
            frame[2] = address;
            frame[3] = functionCode;
            frame[4] = (byte)data.Length;

            Array.Copy(data, 0, frame, 5, data.Length);

            // 计算CRC校验
            ushort crc = CalculateCRC16(frame, frameLength - 2);
            frame[frameLength - 2] = (byte)(crc >> 8);
            frame[frameLength - 1] = (byte)(crc & 0xFF);

            return frame;
        }

        /// <summary>
        /// 构建读取帧
        /// </summary>
        /// <param name="address">设备地址</param>
        /// <param name="functionCode">功能码</param>
        /// <returns>读取帧</returns>
        private byte[] BuildReadFrame(byte address, byte functionCode)
        {
            return BuildFrame(address, functionCode, Array.Empty<byte>());
        }

        /// <summary>
        /// 计算CRC16校验
        /// </summary>
        private ushort CalculateCRC16(byte[] data, int length)
        {
            ushort crc = ProtocolConstants.CRC_INIT_VALUE;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ ProtocolConstants.CRC_POLYNOMIAL);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        #endregion

        #region 读取操作

        /// <summary>
        /// 读取设备序列号
        /// </summary>
        /// <returns>是否发送成功</returns>
        public bool ReadSerialNumber()
        {
            return SendReadCommand(ProtocolConstants.FUNC_READ_SERIAL_NUMBER, "读取设备序列号");
        }

        /// <summary>
        /// 读取电堆信息
        /// </summary>
        /// <returns>是否发送成功</returns>
        public bool ReadStackInfo()
        {
            return SendReadCommand(ProtocolConstants.FUNC_READ_STACK_INFO, "读取电堆信息");
        }

        /// <summary>
        /// 发送读取命令
        /// </summary>
        /// <param name="functionCode">功能码</param>
        /// <param name="operationName">操作名称</param>
        /// <returns>是否发送成功</returns>
        private bool SendReadCommand(byte functionCode, string operationName)
        {
            try
            {
                if (!_serialPortManager.IsOpen)
                {
                    ErrorOccurred?.Invoke(this, "串口未打开");
                    return false;
                }

                byte[] frame = BuildReadFrame(_deviceAddress, functionCode);
                bool success = _serialPortManager.SendData(frame);

                if (success)
                {
                    StatusChanged?.Invoke(this, $"已发送{operationName}命令");
                }

                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"{operationName}失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 设置操作

        /// <summary>
        /// 设置参数
        /// </summary>
        /// <param name="functionCode">功能码</param>
        /// <param name="data">数据</param>
        /// <param name="parameterName">参数名称</param>
        /// <returns>是否发送成功</returns>
        public bool SetParameter(byte functionCode, byte[] data, string parameterName)
        {
            try
            {
                if (!_serialPortManager.IsOpen)
                {
                    ErrorOccurred?.Invoke(this, "串口未打开");
                    return false;
                }

                byte[] frame = BuildFrame(_deviceAddress, functionCode, data);
                bool success = _serialPortManager.SendData(frame);

                if (success)
                {
                    StatusChanged?.Invoke(this, $"已发送设置{parameterName}命令");
                }

                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"设置{parameterName}失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置压力参数
        /// </summary>
        /// <param name="data">压力参数数据</param>
        /// <param name="parameterName">参数名称</param>
        /// <returns>是否发送成功</returns>
        public bool SetPressureParameter(byte[] data, string parameterName)
        {
            return SetParameter(ProtocolConstants.FUNC_SET_PRESSURE_PARAMS, data, parameterName);
        }

        /// <summary>
        /// 设置阀门参数
        /// </summary>
        /// <param name="data">阀门参数数据</param>
        /// <param name="parameterName">参数名称</param>
        /// <returns>是否发送成功</returns>
        public bool SetValveParameter(byte[] data, string parameterName)
        {
            return SetParameter(ProtocolConstants.FUNC_SET_VALVE_PARAMS, data, parameterName);
        }

        /// <summary>
        /// 设置DCDC参数
        /// </summary>
        /// <param name="data">DCDC参数数据</param>
        /// <param name="parameterName">参数名称</param>
        /// <returns>是否发送成功</returns>
        public bool SetDCDCParameter(byte[] data, string parameterName)
        {
            return SetParameter(ProtocolConstants.FUNC_SET_DCDC_PARAMS, data, parameterName);
        }

        #endregion

        #region 系统控制

        /// <summary>
        /// 启动电堆
        /// </summary>
        /// <returns>是否发送成功</returns>
        public bool StartStack()
        {
            return SendReadCommand(ProtocolConstants.FUNC_START_STACK, "启动电堆");
        }

        /// <summary>
        /// 停止电堆
        /// </summary>
        /// <returns>是否发送成功</returns>
        public bool StopStack()
        {
            return SendReadCommand(ProtocolConstants.FUNC_STOP_STACK, "停止电堆");
        }

        #endregion
    }
}
