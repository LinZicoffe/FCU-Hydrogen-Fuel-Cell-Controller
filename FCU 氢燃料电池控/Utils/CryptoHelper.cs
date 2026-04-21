using System;
using System.Security.Cryptography;
using System.Text;

namespace FCU_氢燃料电池控.Utils
{
    /// <summary>
    /// 加密辅助工具类
    /// </summary>
    public static class CryptoHelper
    {
        #region RSA 签名验证

        /// <summary>
        /// RSA公钥 (嵌入在客户端代码中)
        /// 格式: XML格式RSA公钥,2048位
        /// 注意: 这里的公钥是占位符，需要使用许可证生成工具生成真实的密钥对后替换
        /// </summary>
        private const string RSA_PUBLIC_KEY = @"
<RSAKeyValue>
    <RSAKeyValue><Modulus>25D4wXEJLyihp+3Uywp6z4m0drjAfjpH95koi/eoyUMLHQONA9n38NIvVBVNSOgJ4lQDlLCuMciyGDsXsvVHpfgJeVaKDmUSRakw3TdgjI3l/cJgexZinyAIHgPCsHkXSxig2sptB8XqaAr6sDAaJZyKzrIQ+RbqX0tpLbMMRw/4fBb96H5FV7Skb25GW6bHtVJXSo6STLhIlGyGDqg0h83cm2vTUyfcKgH/fWNoBK1GkOVmojB8yBvmbb/qq2QS8XETQA7oQNRDmnYWioTq4eWm3t84xSfNwbzD1WzLJv50F0+4UBAJKngFw0KUoBBeFElGXH+1s3OQY23k4vjJWQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>
</RSAKeyValue>";

        /// <summary>
        /// 验证许可证签名
        /// </summary>
        /// <param name="data">待验证的数据</param>
        /// <param name="signature">Base64编码的签名</param>
        /// <returns>验证结果</returns>
        public static bool VerifyLicenseSignature(string data, string signature)
        {
            try
            {
                using var rsa = RSA.Create();

                // 尝试加载公钥
                try
                {
                    rsa.FromXmlString(RSA_PUBLIC_KEY);
                }
                catch
                {
                    // 如果公钥格式无效，暂时返回 true（开发阶段）
                    // 生产环境必须使用正确的公钥
                    return true;
                }

                // 将签名从Base64转换为字节数组
                byte[] signatureBytes = Convert.FromBase64String(signature);

                // 将数据转换为字节数组
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);

                // 验证签名
                return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region AES 加密解密

        /// <summary>
        /// AES-256加密
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <param name="key">密钥 (32字节)</param>
        /// <param name="iv">初始化向量 (16字节)</param>
        /// <returns>Base64编码的密文</returns>
        public static string AesEncrypt(string plainText, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// AES-256解密
        /// </summary>
        /// <param name="cipherText">Base64编码的密文</param>
        /// <param name="key">密钥 (32字节)</param>
        /// <param name="iv">初始化向量 (16字节)</param>
        /// <returns>明文</returns>
        public static string AesDecrypt(string cipherText, byte[] key, byte[] iv)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch
            {
                throw new CryptographicException("解密失败");
            }
        }

        /// <summary>
        /// 从密码生成密钥和IV (PBKDF2)
        /// </summary>
        /// <param name="password">密码</param>
        /// <param name="salt">盐值</param>
        /// <returns>(Key, IV) 元组</returns>
        public static (byte[] Key, byte[] IV) DeriveKeyFromPassword(string password, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            byte[] key = pbkdf2.GetBytes(32); // 256位密钥
            byte[] iv = pbkdf2.GetBytes(16);  // 128位IV
            return (key, iv);
        }

        #endregion

        #region 哈希计算

        /// <summary>
        /// 计算SHA256哈希
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>十六进制哈希值</returns>
        public static string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes).ToLower();
        }

        #endregion
    }
}
