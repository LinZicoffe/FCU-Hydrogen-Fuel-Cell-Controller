using System;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text.Json;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Utils
{
    /// <summary>
    /// 安全存储工具类 - 使用加密的IsolatedStorage
    /// </summary>
    public static class SecureStorage
    {
        private const string LICENSE_FILE = "license.encrypted";
        private const string TRIAL_FILE = "trial.encrypted";
        private const string REGISTRATION_FILE = "registration.encrypted";
        private static readonly byte[] StorageSalt = new byte[]
            { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };

        private static string RegistrationDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FCU氢燃料电池控制系统");

        private static string RegistrationFilePath => Path.Combine(RegistrationDirectoryPath, REGISTRATION_FILE);

        /// <summary>
        /// 保存许可证数据 (加密存储)
        /// </summary>
        public static void SaveLicense(LicenseData license)
        {
            try
            {
                var json = JsonSerializer.Serialize(license);
                var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_LICENSE_KEY", StorageSalt);
                var encrypted = CryptoHelper.AesEncrypt(json, key, iv);

                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                using var stream = new IsolatedStorageFileStream(LICENSE_FILE, FileMode.Create, isoStore);
                using var writer = new StreamWriter(stream);
                writer.Write(encrypted);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存许可证失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载许可证数据 (解密)
        /// </summary>
        public static LicenseData? LoadLicense()
        {
            try
            {
                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                if (!isoStore.FileExists(LICENSE_FILE))
                    return null;

                using var stream = new IsolatedStorageFileStream(LICENSE_FILE, FileMode.Open, isoStore);
                using var reader = new StreamReader(stream);
                var encrypted = reader.ReadToEnd();

                var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_LICENSE_KEY", StorageSalt);
                var json = CryptoHelper.AesDecrypt(encrypted, key, iv);

                return JsonSerializer.Deserialize<LicenseData>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 删除许可证
        /// </summary>
        public static void DeleteLicense()
        {
            try
            {
                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                if (isoStore.FileExists(LICENSE_FILE))
                    isoStore.DeleteFile(LICENSE_FILE);
            }
            catch
            {
                // 忽略删除失败
            }
        }

        /// <summary>
        /// 保存试用信息 (加密存储)
        /// </summary>
        public static void SaveTrialInfo(TrialInfo trialInfo)
        {
            try
            {
                var json = JsonSerializer.Serialize(trialInfo);
                var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_TRIAL_KEY", StorageSalt);
                var encrypted = CryptoHelper.AesEncrypt(json, key, iv);

                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                using var stream = new IsolatedStorageFileStream(TRIAL_FILE, FileMode.Create, isoStore);
                using var writer = new StreamWriter(stream);
                writer.Write(encrypted);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存试用信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载试用信息 (解密)
        /// </summary>
        public static TrialInfo? LoadTrialInfo()
        {
            try
            {
                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                if (!isoStore.FileExists(TRIAL_FILE))
                    return null;

                using var stream = new IsolatedStorageFileStream(TRIAL_FILE, FileMode.Open, isoStore);
                using var reader = new StreamReader(stream);
                var encrypted = reader.ReadToEnd();

                var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_TRIAL_KEY", StorageSalt);
                var json = CryptoHelper.AesDecrypt(encrypted, key, iv);

                return JsonSerializer.Deserialize<TrialInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 删除试用信息
        /// </summary>
        public static void DeleteTrialInfo()
        {
            try
            {
                using var isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
                if (isoStore.FileExists(TRIAL_FILE))
                    isoStore.DeleteFile(TRIAL_FILE);
            }
            catch
            {
                // 忽略删除失败
            }
        }

        /// <summary>
        /// 检查当前电脑是否已经完成公司注册。
        /// </summary>
        public static bool RegistrationExists()
        {
            return File.Exists(RegistrationFilePath);
        }

        /// <summary>
        /// 首次将公司注册信息保存到机器公共数据目录。文件已存在时拒绝覆盖。
        /// </summary>
        public static bool TrySaveRegistration(RegistrationData registration)
        {
            if (File.Exists(RegistrationFilePath))
            {
                return false;
            }

            var json = JsonSerializer.Serialize(registration);
            var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_REGISTRATION_KEY", StorageSalt);
            var encrypted = CryptoHelper.AesEncrypt(json, key, iv);

            try
            {
                Directory.CreateDirectory(RegistrationDirectoryPath);
                using var stream = new FileStream(RegistrationFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(encrypted);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// 加载已保存的公司注册信息。
        /// </summary>
        public static RegistrationData? LoadRegistration()
        {
            try
            {
                if (!File.Exists(RegistrationFilePath))
                {
                    return null;
                }

                using var stream = new FileStream(RegistrationFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var encrypted = reader.ReadToEnd();

                var (key, iv) = CryptoHelper.DeriveKeyFromPassword("FCU_REGISTRATION_KEY", StorageSalt);
                var json = CryptoHelper.AesDecrypt(encrypted, key, iv);
                return JsonSerializer.Deserialize<RegistrationData>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
