using System;
using System.Collections.Generic;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// 协议处理器 - 负责帧接收、校验和解析
    /// </summary>
    public class ProtocolHandler
    {
        /// <summary>
        /// 接收状态枚举
        /// </summary>
        private enum ReceiveState
        {
            Idle,           // 空闲状态
            Header1,        // 接收帧头第1字节
            Header2,        // 接收帧头第2字节
            Address,        // 接收设备地址
            FunctionCode,   // 接收功能码
            DataLength,     // 接收数据长度
            Data,           // 接收数据域
            CRC_High,       // 接收CRC高字节
            CRC_Low         // 接收CRC低字节
        }

        private ReceiveState _state = ReceiveState.Idle;
        private byte[] _receiveBuffer = new byte[300];
        private int _bufferIndex = 0;
        private int _dataLength = 0;
        private int _lastByteTick = 0;

        /// <summary>
        /// 帧接收超时(毫秒)。与下位机 FCU_RS_485_FRAME_TIMEOUT_MS 保持一致。
        /// 超时后丢弃残帧，避免半截帧把后续帧的前几字节吃掉造成连续解析失败。
        /// </summary>
        private const int FrameTimeoutMs = 1000;

        /// <summary>
        /// 当前是否处于帧接收空闲状态。可用于总线仲裁判断，避免在半接收状态下插入其它协议的请求。
        /// </summary>
        public bool IsIdle => _state == ReceiveState.Idle;

        /// <summary>
        /// 帧接收完成事件
        /// </summary>
        public event Action<byte[]>? FrameReceived;

        /// <summary>
        /// 帧校验失败事件
        /// </summary>
        public event Action? FrameValidationFailed;

        /// <summary>
        /// 处理接收到的字节
        /// </summary>
        /// <param name="data">接收的字节</param>
        public void ProcessReceivedByte(byte data)
        {
            try
            {
                // 帧间超时自愈: 上一帧被截断(掉线/干扰)时状态机会停在中途，
                // 若不复位，下一帧的前几字节会被当作残帧的数据域吞掉，导致连续多帧失败。
                // 下位机 fcu_process_rx_state_machine() 有同样的 1000ms 超时保护。
                int now = Environment.TickCount;
                if (_state != ReceiveState.Idle &&
                    unchecked(now - _lastByteTick) > FrameTimeoutMs)
                {
                    Reset();
                }
                _lastByteTick = now;

                switch (_state)
                {
                    case ReceiveState.Idle:
                        if (data == 0xAA)
                        {
                            _receiveBuffer[0] = data;
                            _bufferIndex = 1;
                            _state = ReceiveState.Header1;
                        }
                        break;

                    case ReceiveState.Header1:
                        if (data == 0x55)
                        {
                            _receiveBuffer[1] = data;
                            _bufferIndex = 2;
                            _state = ReceiveState.Address;
                        }
                        else
                        {
                            // 帧头错误，重置
                            _state = ReceiveState.Idle;
                        }
                        break;

                    case ReceiveState.Address:
                        _receiveBuffer[_bufferIndex++] = data;
                        _state = ReceiveState.FunctionCode;
                        break;

                    case ReceiveState.FunctionCode:
                        _receiveBuffer[_bufferIndex++] = data;
                        _state = ReceiveState.DataLength;
                        break;

                    case ReceiveState.DataLength:
                        _receiveBuffer[_bufferIndex++] = data;
                        _dataLength = data;
                        if (_dataLength == 0)
                        {
                            // 无数据域，直接接收CRC
                            _state = ReceiveState.CRC_High;
                        }
                        else
                        {
                            _state = ReceiveState.Data;
                        }
                        break;

                    case ReceiveState.Data:
                        _receiveBuffer[_bufferIndex++] = data;
                        if (_bufferIndex >= (5 + _dataLength))
                        {
                            _state = ReceiveState.CRC_High;
                        }
                        break;

                    case ReceiveState.CRC_High:
                        _receiveBuffer[_bufferIndex++] = data;
                        _state = ReceiveState.CRC_Low;
                        break;

                    case ReceiveState.CRC_Low:
                        _receiveBuffer[_bufferIndex++] = data;
                        // 帧接收完成，进行校验和处理
                        ProcessCompleteFrame();
                        _state = ReceiveState.Idle;
                        break;
                }
            }
            catch
            {
                // 异常时重置状态机
                _state = ReceiveState.Idle;
            }
        }

        /// <summary>
        /// 处理完整的帧
        /// </summary>
        private void ProcessCompleteFrame()
        {
            try
            {
                int frameLength = _bufferIndex;

                // 验证帧
                if (!ValidateFrame(_receiveBuffer, frameLength))
                {
                    FrameValidationFailed?.Invoke();
                    return;
                }

                // 提取完整帧
                byte[] frame = new byte[frameLength];
                Array.Copy(_receiveBuffer, frame, frameLength);

                // 触发帧接收完成事件
                FrameReceived?.Invoke(frame);
            }
            catch
            {
                // 处理异常，忽略该帧
                FrameValidationFailed?.Invoke();
            }
        }

        /// <summary>
        /// 验证帧的完整性
        /// </summary>
        /// <param name="frame">帧数据</param>
        /// <param name="length">帧长度</param>
        /// <returns>验证结果</returns>
        private bool ValidateFrame(byte[] frame, int length)
        {
            // 1. 检查最小长度
            if (length < 7)
                return false;

            // 2. 检查帧头
            if (frame[0] != 0xAA || frame[1] != 0x55)
                return false;

            // 3. 检查数据长度一致性
            byte dataLen = frame[4];
            if (length != (7 + dataLen))
                return false;

            // 4. 检查CRC
            ushort calcCrc = CalculateCRC16(frame, length - 2);
            ushort recvCrc = (ushort)((frame[length - 2] << 8) | frame[length - 1]);

            if (calcCrc != recvCrc)
                return false;

            return true;
        }

        /// <summary>
        /// 计算CRC16-CCITT校验
        /// </summary>
        /// <param name="data">待校验数据</param>
        /// <param name="length">数据长度</param>
        /// <returns>CRC校验值</returns>
        private ushort CalculateCRC16(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        /// <summary>
        /// 重置接收状态机
        /// </summary>
        public void Reset()
        {
            _state = ReceiveState.Idle;
            _bufferIndex = 0;
            _dataLength = 0;
        }

        /// <summary>
        /// 构建请求帧（静态方法）
        /// </summary>
        /// <param name="deviceAddress">设备地址</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="dataField">数据域</param>
        /// <returns>完整的请求帧</returns>
        public static byte[] BuildRequestFrame(byte deviceAddress, byte functionCode, byte[] dataField)
        {
            byte dataLength = (byte)(dataField?.Length ?? 0);
            int frameLength = 7 + dataLength; // 帧头(2) + 地址(1) + 功能码(1) + 长度(1) + 数据(N) + CRC(2)
            
            byte[] frame = new byte[frameLength];
            
            // 构建帧头
            frame[0] = 0xAA;
            frame[1] = 0x55;
            frame[2] = deviceAddress;
            frame[3] = functionCode;
            frame[4] = dataLength;
            
            // 复制数据域
            if (dataLength > 0 && dataField != null)
            {
                Array.Copy(dataField, 0, frame, 5, dataLength);
            }
            
            // 计算CRC16
            ushort crc = CalculateCRC16Static(frame, frameLength - 2);
            frame[frameLength - 2] = (byte)((crc >> 8) & 0xFF);  // CRC高字节
            frame[frameLength - 1] = (byte)(crc & 0xFF);         // CRC低字节
            
            return frame;
        }

        /// <summary>
        /// 计算CRC16-CCITT校验（静态方法）
        /// </summary>
        /// <param name="data">待校验数据</param>
        /// <param name="length">数据长度</param>
        /// <returns>CRC校验值</returns>
        private static ushort CalculateCRC16Static(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}
