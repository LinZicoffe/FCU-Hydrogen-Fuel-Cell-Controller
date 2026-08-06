using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// 通信帧方向。
    /// </summary>
    public enum CommunicationDirection
    {
        Tx,
        Rx
    }

    /// <summary>
    /// 一条完整的串口通信帧日志。
    /// </summary>
    public sealed class CommunicationLogEntry
    {
        public CommunicationLogEntry(
            DateTime timestamp,
            CommunicationDirection direction,
            byte[] frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            Timestamp = timestamp;
            Direction = direction;
            Frame = (byte[])frame.Clone();
            HexFrame = BitConverter.ToString(Frame).Replace('-', ' ');
            DisplayText = $"[{Timestamp:HH:mm:ss.fff}] {Direction.ToString().ToUpperInvariant()} | {HexFrame}";
        }

        public DateTime Timestamp { get; }

        public CommunicationDirection Direction { get; }

        public byte[] Frame { get; }

        public string HexFrame { get; }

        public string DisplayText { get; }
    }
}
