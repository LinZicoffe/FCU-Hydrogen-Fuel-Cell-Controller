using System;
using System.Buffers.Binary;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>固件main@9bdddb0，独立0x0302；不改变0x0301。</summary>
    public static class CurrentCalibrationProtocol
    {
        public static byte[] Read(byte address) => ProtocolHandler.BuildRequestFrame(address, 0x0A, new byte[] { 3, 2, 0 });

        public static byte[] Write(byte address, CurrentCalibration values)
        {
            if (!values.IsValid) throw new ArgumentOutOfRangeException(nameof(values), "k须为有限正数，b须为有限数");
            byte[] data = new byte[10];
            data[0] = 3; data[1] = 2;
            BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(2, 4), values.K);
            BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(6, 4), values.B);
            return ProtocolHandler.BuildRequestFrame(address, 0x05, data);
        }

        public static bool TryParse(byte[] frame, byte address, out CurrentCalibration? values)
        {
            values = null;
            if (!FirmwareMainProtocol.IsResponse(frame, address, 0x8A, 11) ||
                frame[5] != 3 || frame[6] != 2 || frame[7] != 0) return false;
            var parsed = new CurrentCalibration(BinaryPrimitives.ReadSingleBigEndian(frame.AsSpan(8, 4)),
                BinaryPrimitives.ReadSingleBigEndian(frame.AsSpan(12, 4)));
            if (!parsed.IsValid) return false;
            values = parsed;
            return true;
        }
    }
}
