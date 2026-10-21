using System;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>普通参数的写入/回读布局，以固件 fcu_protocol.c 的编码为准。</summary>
    public static class ParameterWriteProtocol
    {
        public static ParameterWriteRequest Create(byte address, byte function, byte[] data, string name)
        {
            if (address > 247 || data == null) throw new ArgumentException("设备地址或参数数据无效");
            ushort id = function == 0x0E ? (ushort)0x0601 : data.Length >= 2
                ? (ushort)((data[0] << 8) | data[1]) : (ushort)0;
            int size = (function, id) switch
            {
                (0x02, 0x0101) => 18,
                (0x03, 0x0201) => 12,
                (0x04, 0x0501) when data.Length >= 3 && data[2] is >= 1 and <= 15 => 3 + data[2] * 4,
                (0x04, 0x0502) => 11,
                (0x04, 0x0503) => 7,
                (0x05, 0x0301) => 6,
                (0x06, 0x0401) => 13,
                (0x0B, 0x0901) => 4,
                (0x0E, 0x0601) => 12,
                _ => throw new ArgumentException("该参数不属于普通参数写入通道")
            };
            if (data.Length != size) throw new ArgumentException($"参数0x{id:X4}的写入数据长度应为{size}字节");
            // 0x0501 的条数只出现在写帧中；0x0601 的写入体不含参数ID。
            int bodyOffset = function == 0x0E ? 0 : id == 0x0501 ? 3 : 2;
            return new ParameterWriteRequest(address, id, name, function == 0x0E ? (byte)0x8E : (byte)0x82,
                ProtocolHandler.BuildRequestFrame(address, function, data),
                ProtocolHandler.BuildRequestFrame(address, 0x0A, new byte[] { (byte)(id >> 8), (byte)id, 0 }),
                data[bodyOffset..]);
        }

        public static bool IsValidReadback(byte[] frame, ParameterWriteRequest request) =>
            FirmwareMainProtocol.IsResponse(frame, request.Address, 0x8A, request.ExpectedBody.Length + 3) &&
            frame[5] == (byte)(request.ParameterId >> 8) && frame[6] == (byte)request.ParameterId && frame[7] == 0;
    }
}
