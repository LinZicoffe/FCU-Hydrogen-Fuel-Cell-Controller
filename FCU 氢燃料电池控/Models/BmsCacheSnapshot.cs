namespace FCU_氢燃料电池控.Models
{
    /// <summary>固件main 0x91缓存快照；旧值可以诊断，不能把失效值当作实时测量。</summary>
    public sealed record BmsCacheSnapshot(byte Flags, byte Soc, short CurrentRaw,
        ushort VoltageRaw, ushort Alarm, ushort Protect, ushort Fault, ushort SystemStatus, uint AgeMs)
    {
        public bool Online => (Flags & 1) != 0 && AgeMs <= 6000;
        public bool SocValid => Online && (Flags & 2) != 0 && Soc <= 100;
        public double Current => CurrentRaw * 0.01;
        public double Voltage => VoltageRaw * 0.01;
    }
}
