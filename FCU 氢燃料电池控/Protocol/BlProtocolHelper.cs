using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// Bootloader 升级协议帧处理工具类
    /// 帧格式与固件 BL/Src/bl_proto.c 及 H7 参考工具 IapProtocolHelper.cs 逐字节一致:
    ///   请求: AA 55 + Cmd(1B) + Len(2B 小端) + Data + Checksum(2B 小端, 从帧头累加到 Data 末尾)
    ///   应答: AA 55 + Status(1B) + Len(2B 小端) + Data + Checksum(2B 小端)
    /// 创建日期: 2026-08-19
    /// 依据: docs/03-设计方案/FCU-RS485-Bootloader在线升级设计方案.md §3.2/§3.3
    /// </summary>
    public static class BlProtocolHelper
    {
        public const byte HEADER_BYTE_0 = 0xAA;
        public const byte HEADER_BYTE_1 = 0x55;
        public const int MIN_FRAME_LENGTH = 7;

        /// <summary>Write 命令数据域上限 = 地址(4B) + 数据块(1024B), BL 侧 LEN 前置校验同值</summary>
        public const int WRITE_DATA_MAX = 4 + 1024;

        /// <summary>命令码定义 (H7 命令集 + BL 新增 Verify)</summary>
        public static class Commands
        {
            public const byte GetUid = 0x01;
            public const byte EraseApp = 0x02;
            public const byte Write = 0x03;
            public const byte WriteMeta = 0x04;
            public const byte Reset = 0x05;
            public const byte GetVersion = 0x06;
            /// <summary>BL 新增: 对 APP 区前 N 字节计算 CRC32 返回, 替代整区读回校验</summary>
            public const byte Verify = 0x07;
        }

        /// <summary>应答 Status 码</summary>
        public static class Status
        {
            public const byte Ok = 0x00;
            public const byte Fail = 0x01;
        }

        /// <summary>
        /// 构建请求帧: [Header 2B][Cmd 1B][Length 2B 小端][Data][Checksum 2B 小端]
        /// </summary>
        public static byte[] BuildRequestFrame(byte cmd, byte[]? data)
        {
            int dataLen = data?.Length ?? 0;
            byte[] frame = new byte[MIN_FRAME_LENGTH + dataLen];

            frame[0] = HEADER_BYTE_0;
            frame[1] = HEADER_BYTE_1;
            frame[2] = cmd;
            frame[3] = (byte)(dataLen & 0xFF);
            frame[4] = (byte)((dataLen >> 8) & 0xFF);
            if (dataLen > 0 && data != null)
            {
                Array.Copy(data, 0, frame, 5, dataLen);
            }

            ushort checksum = CalculateChecksum(frame, frame.Length - 2);
            frame[frame.Length - 2] = (byte)(checksum & 0xFF);
            frame[frame.Length - 1] = (byte)((checksum >> 8) & 0xFF);

            return frame;
        }

        /// <summary>
        /// 校验应答帧结构完整性 (不含业务语义): 帧头/长度一致/累加和
        /// </summary>
        public static bool ParseResponse(byte[] buffer, int length, out byte[]? data, out byte status)
        {
            if (!TryParseResponse(buffer, length, out data, out status))
                return false;
            return status == Status.Ok;
        }

        /// <summary>
        /// 纯结构校验 (帧头/长度/累加和): 通过时 status 为帧内 Status 字段 (含 0x01 显式拒绝),
        /// 供接收状态机区分"结构失败丢弃"与"结构有效但 Status=0x01 的有效应答"
        /// </summary>
        public static bool TryParseResponse(byte[] buffer, int length, out byte[]? data, out byte status)
        {
            data = null;
            status = 0xFF;

            if (length < MIN_FRAME_LENGTH)
                return false;
            if (buffer[0] != HEADER_BYTE_0 || buffer[1] != HEADER_BYTE_1)
                return false;

            status = buffer[2];
            ushort dataLen = (ushort)((buffer[4] << 8) | buffer[3]);

            if (length != MIN_FRAME_LENGTH + dataLen)
                return false;

            ushort recvChecksum = (ushort)((buffer[length - 1] << 8) | buffer[length - 2]);
            ushort calcChecksum = CalculateChecksum(buffer, length - 2);
            if (recvChecksum != calcChecksum)
                return false;

            if (dataLen > 0)
            {
                data = new byte[dataLen];
                Array.Copy(buffer, 5, data, 0, dataLen);
            }

            return true;
        }

        /// <summary>
        /// 16 位累加和: 对 [0, length) 字节累加 (与固件 bl_checksum / H7 CalculateChecksum 等价)
        /// </summary>
        public static ushort CalculateChecksum(byte[] data, int length)
        {
            ushort sum = 0;
            for (int i = 0; i < length && i < data.Length; i++)
            {
                sum += data[i];
            }
            return sum;
        }

        /// <summary>
        /// 构建 Write 命令数据域: 4B 地址(小端) + 数据块
        /// </summary>
        public static byte[] BuildWriteData(uint address, byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 1024)
                throw new ArgumentException($"Write 数据块长度非法: {data?.Length ?? 0} (合法 1~1024)");

            var cmdData = new byte[4 + data.Length];
            cmdData[0] = (byte)(address & 0xFF);
            cmdData[1] = (byte)((address >> 8) & 0xFF);
            cmdData[2] = (byte)((address >> 16) & 0xFF);
            cmdData[3] = (byte)((address >> 24) & 0xFF);
            Array.Copy(data, 0, cmdData, 4, data.Length);
            return cmdData;
        }

        /// <summary>
        /// CRC32 (MSB-first 非反射, 即 CRC-32/BZIP2 参数组)
        /// 与固件 BL/Src/bl_meta.c bl_crc32 及 App/Src/fcu_flash.c fcu_crc32_sw 逐位一致:
        /// poly 0x04C11DB7 / init 0xFFFFFFFF / xorout 0xFFFFFFFF / refin=false / refout=false
        /// 注意: 不得替换为 System.IO.Hashing.Crc32 等 zlib 反射实现
        /// </summary>
        public static uint Crc32Mpeg2(byte[] data, int length)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < length && i < data.Length; i++)
            {
                crc ^= (uint)data[i] << 24;
                for (int b = 0; b < 8; b++)
                {
                    if ((crc & 0x80000000u) != 0u)
                        crc = (crc << 1) ^ 0x04C11DB7u;
                    else
                        crc <<= 1;
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>APP 分区基址与上限 (BL 白名单同值)</summary>
        public const uint APP_BASE = 0x08004000u;
        public const uint APP_END = 0x0801E000u;
        public const uint APP_SIZE_MAX = APP_END - APP_BASE;   // 0x1A000 = 106496

        /// <summary>META magic ('F','C','U','B' 依次写入, 小端)</summary>
        public const uint META_MAGIC = 0x46435542u;
    }
}
