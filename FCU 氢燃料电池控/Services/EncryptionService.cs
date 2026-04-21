using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// 加密服务 - 提供额外的加密功能
    /// </summary>
    public static class EncryptionService
    {
        /// <summary>
        /// 混淆字符串 (用于隐藏关键字符串)
        /// </summary>
        public static string ObfuscateString(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var key = Encoding.UTF8.GetBytes("FCU_OBFUSCATE_KEY_2024");

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= key[i % key.Length];
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 反混淆字符串
        /// </summary>
        public static string DeobfuscateString(string obfuscated)
        {
            var bytes = Convert.FromBase64String(obfuscated);
            var key = Encoding.UTF8.GetBytes("FCU_OBFUSCATE_KEY_2024");

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= key[i % key.Length];
            }

            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 生成随机盐值
        /// </summary>
        public static byte[] GenerateRandomSalt(int size = 16)
        {
            var salt = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);
            return salt;
        }

        /// <summary>
        /// 计算文件哈希 (用于完整性验证)
        /// </summary>
        public static string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLower();
        }

        /// <summary>
        /// 生成随机字符串
        /// </summary>
        public static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var result = new StringBuilder(length);
            using var rng = RandomNumberGenerator.Create();

            for (int i = 0; i < length; i++)
            {
                var randomBytes = new byte[4];
                rng.GetBytes(randomBytes);
                var randomIndex = BitConverter.ToUInt32(randomBytes, 0) % chars.Length;
                result.Append(chars[(int)randomIndex]);
            }

            return result.ToString();
        }
    }
}
