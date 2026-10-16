using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>实测输入电流(A)=K×原始命令电流(A)+B；反算由固件负责。</summary>
    public sealed record CurrentCalibration(float K, float B)
    {
        public bool IsValid => float.IsFinite(K) && K > 0 && float.IsFinite(B);
    }
}
