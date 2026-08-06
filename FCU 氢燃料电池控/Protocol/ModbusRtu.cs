using System;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// Modbus RTU 协议层 - 纯静态无状态工具类
    /// 创建日期: 2026-08-06
    /// 用途: 上位机作为 Modbus 主站访问 BMS (V1.28-S, 仅支持 03H/10H)
    /// [BMS协议 1.28-S §2.1/§2.2]
    /// </summary>
    public static class ModbusRtu
    {
        /// <summary>功能码: 读保持寄存器</summary>
        public const byte FUNC_READ_HOLDING = 0x03;

        /// <summary>功能码: 写多个寄存器(本协议用于写单个特殊功能寄存器)</summary>
        public const byte FUNC_WRITE_MULTIPLE = 0x10;

        /// <summary>异常响应标志: 功能码 | 0x80</summary>
        public const byte EXCEPTION_FLAG = 0x80;

        /// <summary>异常响应帧长: addr(1)+func(1)+errcode(1)+crc(2)</summary>
        public const int EXCEPTION_FRAME_LEN = 5;

        /// <summary>03H 请求帧长: addr+func+start(2)+count(2)+crc(2)</summary>
        public const int READ_REQUEST_LEN = 8;

        /// <summary>10H 请求帧长(写1个寄存器): addr+func+reg(2)+count(2)+bytecount(1)+data(2)+crc(2)</summary>
        public const int WRITE_REQUEST_LEN = 11;

        /// <summary>10H 成功响应帧长: addr+func+regstart(2)+count(2)+crc(2), 不含bytecount与数据域</summary>
        public const int WRITE_RESPONSE_LEN = 8;

        /// <summary>
        /// 解析结果
        /// </summary>
        public enum ParseResult
        {
            /// <summary>缓冲中未找到匹配帧(可能全是噪声字节)</summary>
            None,
            /// <summary>已定位帧头但数据不足, 需等待更多字节</summary>
            NeedMoreData,
            /// <summary>成功解析一帧正常响应</summary>
            Frame,
            /// <summary>收到 BMS 异常响应(func|0x80)</summary>
            Exception
        }

        /// <summary>
        /// 计算 Modbus CRC16 (多项式 0xA001 反向, 初值 0xFFFF)
        /// [BMS协议 1.28-S §2] 低字节在前发送
        /// </summary>
        /// <param name="data">待校验数据</param>
        /// <param name="length">数据长度</param>
        /// <returns>CRC 校验值</returns>
        public static ushort ComputeCrc16(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        /// <summary>
        /// 构造 03H 读保持寄存器请求帧 (8B)
        /// [BMS协议 1.28-S §2.1] 线地址直接发寄存器号
        /// </summary>
        /// <param name="addr">从站地址</param>
        /// <param name="startReg">起始寄存器号</param>
        /// <param name="count">寄存器个数</param>
        /// <returns>完整请求帧</returns>
        public static byte[] BuildReadHoldingRegs(byte addr, ushort startReg, ushort count)
        {
            byte[] frame = new byte[READ_REQUEST_LEN];
            frame[0] = addr;
            frame[1] = FUNC_READ_HOLDING;
            frame[2] = (byte)(startReg >> 8);
            frame[3] = (byte)(startReg & 0xFF);
            frame[4] = (byte)(count >> 8);
            frame[5] = (byte)(count & 0xFF);

            ushort crc = ComputeCrc16(frame, READ_REQUEST_LEN - 2);
            frame[6] = (byte)(crc & 0xFF);          // CRC 低字节在前
            frame[7] = (byte)((crc >> 8) & 0xFF);   // CRC 高字节在后
            return frame;
        }

        /// <summary>
        /// 构造 10H 写寄存器请求帧 (写 1 个寄存器, 11B)
        /// [BMS协议 1.28-S §2.2/§3.3] 特殊功能寄存器: reg1=充电MOS, reg2=放电MOS
        /// </summary>
        /// <param name="addr">从站地址</param>
        /// <param name="reg">寄存器号</param>
        /// <param name="value">写入值 (0xAAAA=开, 0x5555=关)</param>
        /// <returns>完整请求帧</returns>
        public static byte[] BuildWriteSingleReg(byte addr, ushort reg, ushort value)
        {
            byte[] frame = new byte[WRITE_REQUEST_LEN];
            frame[0] = addr;
            frame[1] = FUNC_WRITE_MULTIPLE;
            frame[2] = (byte)(reg >> 8);
            frame[3] = (byte)(reg & 0xFF);
            frame[4] = 0x00;                        // 写寄存器个数 Hi
            frame[5] = 0x01;                        // 写寄存器个数 Lo = 1
            frame[6] = 0x02;                        // 字节数 = 个数*2
            frame[7] = (byte)(value >> 8);
            frame[8] = (byte)(value & 0xFF);

            ushort crc = ComputeCrc16(frame, WRITE_REQUEST_LEN - 2);
            frame[9] = (byte)(crc & 0xFF);
            frame[10] = (byte)((crc >> 8) & 0xFF);
            return frame;
        }

        /// <summary>
        /// 在接收缓冲中滑动扫描匹配的 Modbus 响应帧
        /// 仅在上位机发出请求后的超时窗口内调用; FCU 的 AA55 流量被地址+功能码+CRC 三级过滤
        /// </summary>
        /// <param name="buffer">接收缓冲(可能含前导噪声字节)</param>
        /// <param name="length">缓冲有效长度</param>
        /// <param name="addr">期望的从站地址</param>
        /// <param name="func">期望的功能码 (0x03 或 0x10)</param>
        /// <param name="expectedRegCount">03H 期望的寄存器个数(决定正常帧长); 10H 时忽略</param>
        /// <param name="frame">解析成功时的完整帧(含地址与CRC)</param>
        /// <param name="consumed">应从缓冲丢弃的字节数(含前导噪声)</param>
        /// <param name="exceptionCode">异常响应时的错误码 (1=无效功能码 2=无效寄存器 3=无效数据 4=校验错误 5=写入失败)</param>
        /// <returns>解析结果</returns>
        public static ParseResult TryParseResponse(
            byte[] buffer, int length, byte addr, byte func, int expectedRegCount,
            out byte[]? frame, out int consumed, out byte exceptionCode)
        {
            frame = null;
            consumed = 0;
            exceptionCode = 0;

            // 正常帧长度: 03H = addr+func+bytecount+data(2*count)+crc(2); 10H = 8B 回显
            int normalLen = (func == FUNC_READ_HOLDING)
                ? 5 + 2 * expectedRegCount
                : WRITE_RESPONSE_LEN;

            for (int i = 0; i + 1 < length; i++)
            {
                if (buffer[i] != addr)
                    continue;   // 噪声字节, 跳过

                byte respFunc = buffer[i + 1];

                if (respFunc == (byte)(func | EXCEPTION_FLAG))
                {
                    // 异常响应: 5B
                    if (i + EXCEPTION_FRAME_LEN > length)
                    {
                        consumed = i;   // 丢弃帧头前噪声, 保留帧头续传
                        return ParseResult.NeedMoreData;
                    }
                    if (!CheckCrc(buffer, i, EXCEPTION_FRAME_LEN))
                        continue;   // CRC 不符, 该字节视为噪声

                    frame = new byte[EXCEPTION_FRAME_LEN];
                    Array.Copy(buffer, i, frame, 0, EXCEPTION_FRAME_LEN);
                    exceptionCode = frame[2];
                    consumed = i + EXCEPTION_FRAME_LEN;
                    return ParseResult.Exception;
                }

                if (respFunc != func)
                    continue;   // 非本请求的功能码, 视为噪声

                // 03H 先验字节数域, 提前剔除假帧头
                if (func == FUNC_READ_HOLDING)
                {
                    if (i + 2 >= length)
                    {
                        consumed = i;
                        return ParseResult.NeedMoreData;
                    }
                    if (buffer[i + 2] != (byte)(2 * expectedRegCount))
                        continue;
                }

                if (i + normalLen > length)
                {
                    consumed = i;   // 帧头匹配但数据不足, 等待续传
                    return ParseResult.NeedMoreData;
                }

                if (!CheckCrc(buffer, i, normalLen))
                    continue;   // CRC 不符, 继续找下一个候选

                frame = new byte[normalLen];
                Array.Copy(buffer, i, frame, 0, normalLen);
                consumed = i + normalLen;
                return ParseResult.Frame;
            }

            // 全缓冲无匹配: 保留最后一个字节(可能是被截断的地址字节), 其余丢弃
            consumed = Math.Max(0, length - 1);
            return ParseResult.None;
        }

        /// <summary>
        /// 从 03H 响应帧提取寄存器值 (大端)
        /// </summary>
        /// <param name="frame">完整 03H 响应帧</param>
        /// <param name="regIndex">寄存器索引(0 起)</param>
        /// <returns>寄存器原始值</returns>
        public static ushort GetRegister(byte[] frame, int regIndex)
        {
            int offset = 3 + regIndex * 2;  // 数据域从索引3起
            return (ushort)((frame[offset] << 8) | frame[offset + 1]);
        }

        /// <summary>
        /// 校验 buffer[offset..offset+len) 帧的 CRC (低字节在前)
        /// </summary>
        private static bool CheckCrc(byte[] buffer, int offset, int len)
        {
            ushort calc = ComputeCrc16(buffer.AsSpan(offset, len - 2).ToArray(), len - 2);
            byte lo = buffer[offset + len - 2];
            byte hi = buffer[offset + len - 1];
            return calc == (ushort)(lo | (hi << 8));
        }
    }
}
