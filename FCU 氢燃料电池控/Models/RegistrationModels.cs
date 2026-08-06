using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// 首次运行时保存的公司注册信息。
    /// </summary>
    public class RegistrationData
    {
        public string CompanyName { get; set; } = string.Empty;

        public DateTime RegisteredAtUtc { get; set; }
    }
}
