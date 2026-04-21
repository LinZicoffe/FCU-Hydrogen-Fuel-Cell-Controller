using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCU_氢燃料电池控.Models;

namespace LicenseKeyGenerator
{
    class Program
    {
        // RSA私钥 (仅用于许可证生成工具，不分发)
        // 注意: 首次运行时会生成新的密钥对，请妥善保管私钥
        private static string? RSA_PRIVATE_KEY = null;
        private static string? RSA_PUBLIC_KEY = null;

        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  FCU 氢燃料电池控制系统 - 许可证生成工具");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // 检查是否需要生成新的密钥对
            if (RSA_PRIVATE_KEY == null || RSA_PUBLIC_KEY == null)
            {
                Console.WriteLine("首次运行，正在生成RSA密钥对...");
                GenerateRSAKeyPair();
                Console.WriteLine();
                Console.WriteLine("⚠️  重要提示：");
                Console.WriteLine("1. 请保存上方显示的公钥和私钥");
                Console.WriteLine("2. 私钥必须严格保密，仅用于此生成工具");
                Console.WriteLine("3. 公钥需要复制到客户端 Utils/CryptoHelper.cs 中的 RSA_PUBLIC_KEY 常量");
                Console.WriteLine("4. 建议将公钥和私钥备份到安全位置");
                Console.WriteLine();
                Console.Write("按任意键继续...");
                Console.ReadKey();
                Console.WriteLine();
                Console.Clear();

                // 重新显示标题
                Console.WriteLine("========================================");
                Console.WriteLine("  FCU 氢燃料电池控制系统 - 许可证生成工具");
                Console.WriteLine("========================================");
                Console.WriteLine();
            }

            try
            {
                // 1. 输入客户信息
                Console.Write("请输入客户名称: ");
                var customerName = Console.ReadLine()?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(customerName))
                {
                    Console.WriteLine("错误: 客户名称不能为空");
                    return;
                }

                // 2. 输入硬件指纹
                Console.Write("请输入硬件指纹: ");
                var hardwareId = Console.ReadLine()?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(hardwareId))
                {
                    Console.WriteLine("错误: 硬件指纹不能为空");
                    return;
                }

                if (hardwareId.Length != 64)
                {
                    Console.WriteLine($"错误: 硬件指纹长度应为64位，当前为{hardwareId.Length}位");
                    return;
                }

                // 3. 选择许可证类型
                Console.WriteLine("\n选择许可证类型:");
                Console.WriteLine("1. 标准版");
                Console.WriteLine("2. 专业版");
                Console.WriteLine("3. 企业版");
                Console.Write("请选择 (1-3): ");

                var licenseTypeChoice = Console.ReadLine()?.Trim();
                LicenseType licenseType = licenseTypeChoice switch
                {
                    "1" => LicenseType.Standard,
                    "2" => LicenseType.Professional,
                    "3" => LicenseType.Enterprise,
                    _ => LicenseType.Standard
                };

                // 4. 选择许可证期限
                Console.WriteLine("\n选择许可证期限:");
                Console.WriteLine("1. 1年");
                Console.WriteLine("2. 2年");
                Console.WriteLine("3. 3年");
                Console.WriteLine("4. 永久授权");
                Console.Write("请选择 (1-4): ");

                var periodChoice = Console.ReadLine()?.Trim();
                int years = periodChoice switch
                {
                    "1" => 1,
                    "2" => 2,
                    "3" => 3,
                    "4" => 0,  // 永久授权
                    _ => 1
                };

                // 5. 确认信息
                Console.WriteLine("\n========================================");
                Console.WriteLine("许可证信息确认:");
                Console.WriteLine($"客户名称: {customerName}");
                Console.WriteLine($"硬件指纹: {hardwareId[..32]}...");
                Console.WriteLine($"许可证类型: {licenseType}");
                Console.WriteLine($"有效期: {(years == 0 ? "永久授权" : years + "年")}");
                Console.WriteLine("========================================");
                Console.Write("\n确认生成? (Y/N): ");

                var confirm = Console.ReadLine()?.Trim().ToUpper();
                if (confirm != "Y")
                {
                    Console.WriteLine("已取消");
                    return;
                }

                // 6. 生成许可证
                var license = GenerateLicense(customerName, hardwareId, licenseType, years);

                // 7. 保存许可证文件
                var fileName = $"FCU_License_{customerName}_{DateTime.Now:yyyyMMddHHmmss}.license";
                var json = JsonSerializer.Serialize(license, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fileName, json);

                Console.WriteLine($"\n✓ 许可证已生成并保存到: {fileName}");
                Console.WriteLine("\n许可证详情:");
                Console.WriteLine($"许可证ID: {license.LicenseId}");
                Console.WriteLine($"颁发时间: {DateTimeOffset.FromUnixTimeSeconds(license.IssuedAt):yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"过期时间: {(license.ExpiresAt == 0 ? "永久授权" : DateTimeOffset.FromUnixTimeSeconds(license.ExpiresAt).ToString("yyyy-MM-dd HH:mm:ss"))}");
                Console.WriteLine("\n请将此文件发送给客户。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 生成RSA密钥对
        /// </summary>
        static void GenerateRSAKeyPair()
        {
            using var rsa = RSA.Create(2048);

            // 导出私钥（包含公钥参数）
            RSA_PRIVATE_KEY = rsa.ToXmlString(true);

            // 导出公钥
            RSA_PUBLIC_KEY = rsa.ToXmlString(false);

            Console.WriteLine("\n╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  RSA 公钥 (复制到客户端 Utils/CryptoHelper.cs)                   ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine(RSA_PUBLIC_KEY);
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  RSA 私钥 (严格保密，仅用于本工具)                             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine(RSA_PRIVATE_KEY);
        }

        /// <summary>
        /// 生成许可证
        /// </summary>
        static LicenseData GenerateLicense(string customerName, string hardwareId, LicenseType licenseType, int years)
        {
            var now = DateTimeOffset.UtcNow;
            var license = new LicenseData
            {
                LicenseId = Guid.NewGuid().ToString("N"),
                CustomerName = customerName,
                HardwareId = hardwareId,
                ProductVersion = "1.0.0",
                LicenseType = licenseType,
                IssuedAt = now.ToUnixTimeSeconds(),
                ExpiresAt = years == 0 ? 0 : now.AddYears(years).ToUnixTimeSeconds(),
                Features = licenseType switch
                {
                    LicenseType.Standard => LicenseFeatures.Basic,
                    LicenseType.Professional => LicenseFeatures.Basic | LicenseFeatures.AdvancedConfig | LicenseFeatures.DataLogging,
                    LicenseType.Enterprise => LicenseFeatures.AllFeatures,
                    _ => LicenseFeatures.Basic
                }
            };

            // 生成签名
            var dataToSign = $"{license.LicenseId}|{license.CustomerName}|{license.HardwareId}|{license.ProductVersion}|{(int)license.LicenseType}|{license.IssuedAt}|{license.ExpiresAt}|{(int)license.Features}";
            license.Signature = GenerateSignature(dataToSign);

            return license;
        }

        /// <summary>
        /// 生成RSA签名
        /// </summary>
        static string GenerateSignature(string data)
        {
            if (RSA_PRIVATE_KEY == null)
            {
                throw new InvalidOperationException("RSA私钥未初始化");
            }

            using var rsa = RSA.Create();
            rsa.FromXmlString(RSA_PRIVATE_KEY);

            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return Convert.ToBase64String(signatureBytes);
        }
    }
}
