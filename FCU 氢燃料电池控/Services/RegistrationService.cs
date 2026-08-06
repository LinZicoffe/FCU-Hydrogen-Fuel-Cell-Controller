using System;
using System.Linq;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Utils;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// 管理一次性公司注册。
    /// </summary>
    public class RegistrationService
    {
        public RegistrationData? LoadRegistration()
        {
            if (!SecureStorage.RegistrationExists())
            {
                return null;
            }

            return SecureStorage.LoadRegistration()
                ?? throw new InvalidOperationException("公司注册信息已损坏，请联系技术支持");
        }

        public (bool Success, string Message, RegistrationData? Registration) Register(string companyName)
        {
            var normalizedName = companyName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return (false, "公司名称不能为空", null);
            }

            if (normalizedName.Length > 100)
            {
                return (false, "公司名称不能超过 100 个字符", null);
            }

            if (normalizedName.Any(char.IsControl))
            {
                return (false, "公司名称不能包含控制字符", null);
            }

            var registration = new RegistrationData
            {
                CompanyName = normalizedName,
                RegisteredAtUtc = DateTime.UtcNow
            };

            if (!SecureStorage.TrySaveRegistration(registration))
            {
                return (false, "本机已经完成公司注册，不能重复注册", null);
            }

            return (true, "注册成功", registration);
        }
    }
}
