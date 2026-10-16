using System;
using System.Linq;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>以450V_FCU main@2c8be1d的.c实现为准，不使用旧IAP的0x11/0x91语义。</summary>
    public static class FirmwareMainProtocol
    {
        public static byte[] ReadBmsCache(byte address) =>
            ProtocolHandler.BuildRequestFrame(address, ProtocolConstants.FUNC_READ_BMS_CACHE, Array.Empty<byte>());

        public static byte[] ReadStandbyPeriod(byte address) =>
            ProtocolHandler.BuildRequestFrame(address, ProtocolConstants.FUNC_READ_CONFIG, new byte[] { 9, 2, 0 });

        public static byte[] WriteStandbyPeriod(byte address, ushort seconds)
        {
            if (seconds == 0) throw new ArgumentOutOfRangeException(nameof(seconds), "待机采样周期必须为1～65535秒");
            return ProtocolHandler.BuildRequestFrame(address, ProtocolConstants.FUNC_SET_GENERAL_PARAMS,
                new byte[] { 9, 2, (byte)(seconds >> 8), (byte)seconds });
        }

        public static bool IsResponse(byte[] frame, byte address, byte function, int dataLength)
        {
            if (frame == null || frame.Length != dataLength + 7 || frame[0] != 0xAA || frame[1] != 0x55 ||
                frame[2] != address || frame[3] != function || frame[4] != dataLength) return false;
            // 复用既有CCITT编码器核验CRC，独立调用解析器也不能接收损坏帧。
            return frame.SequenceEqual(ProtocolHandler.BuildRequestFrame(address, function, frame[5..^2]));
        }

        public static bool TryParseBmsCache(byte[] frame, byte address, out BmsCacheSnapshot? snapshot)
        {
            snapshot = null;
            if (!IsResponse(frame, address, ProtocolConstants.RESP_BMS_CACHE, 18)) return false;
            ushort U16(int offset) => (ushort)((frame[offset] << 8) | frame[offset + 1]);
            uint age = ((uint)frame[19] << 24) | ((uint)frame[20] << 16) | ((uint)frame[21] << 8) | frame[22];
            snapshot = new BmsCacheSnapshot(frame[5], frame[6], unchecked((short)U16(7)), U16(9),
                U16(11), U16(13), U16(15), U16(17), age);
            return true;
        }

        public static bool TryParseStandbyPeriod(byte[] frame, byte address, out ushort seconds)
        {
            seconds = 0;
            if (!IsResponse(frame, address, ProtocolConstants.RESP_READ_CONFIG, 5) ||
                frame[5] != 9 || frame[6] != 2 || frame[7] != 0) return false;
            seconds = (ushort)((frame[8] << 8) | frame[9]);
            return seconds != 0;
        }
    }
}
