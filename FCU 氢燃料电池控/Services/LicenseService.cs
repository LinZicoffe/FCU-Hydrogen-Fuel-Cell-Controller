// ======================================================================
// ⚠️ 硬件检查开关
// ======================================================================
// 启用硬件检查：取消下面注释（删除前面的 //）
// 禁用硬件检查：保持注释状态（开发调试时）
// ======================================================================
//#define ENABLE_HARDWARE_CHECK
// ======================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Utils;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// 许可证验证服务
    /// </summary>
    public class LicenseService
    {
        #region 私有字段

        private const int TRIAL_DAYS = 90;
        private const int WARNING_DAYS = 7;  // 到期前7天提示

        private LicenseData? _cachedLicense;
        private TrialInfo? _cachedTrialInfo;

        #endregion

        #region 公共属性

        /// <summary>
        /// 当前硬件指纹
        /// </summary>
        public string CurrentHardwareId { get; private set; } = HardwareInfo.GetHardwareFingerprint();

        /// <summary>
        /// 是否已激活
        /// </summary>
        public bool IsActivated { get; private set; }

        /// <summary>
        /// 许可证验证结果
        /// </summary>
        public LicenseValidationResult? ValidationResult { get; private set; }

        /// <summary>
        /// 试用天数常量
        /// </summary>
        public const int TrialDays = TRIAL_DAYS;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public LicenseService()
        {
            LoadCachedData();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 验证许可证状态 (异步)
        /// </summary>
        public async Task<LicenseValidationResult> ValidateLicenseAsync()
        {
            return await Task.Run(() => ValidateLicense());
        }

        /// <summary>
        /// 验证许可证状态 (同步)
        /// </summary>
        public LicenseValidationResult ValidateLicense()
        {
            var result = new LicenseValidationResult();

            try
            {
                // 1. 尝试加载已激活的许可证
                var license = SecureStorage.LoadLicense();
                if (license != null)
                {
                    var licenseResult = ValidateActivatedLicense(license);
                    if (licenseResult.IsValid)
                    {
                        _cachedLicense = license;
                        IsActivated = true;
                        ValidationResult = licenseResult;
                        return licenseResult;
                    }
                    // 许可证无效，删除并继续检查试用
                    SecureStorage.DeleteLicense();
                }

                // 2. 检查试用状态
                var trialInfo = SecureStorage.LoadTrialInfo();
                var trialResult = ValidateTrialLicense(trialInfo);

                _cachedTrialInfo = trialInfo;
                IsActivated = false;
                ValidationResult = trialResult;

                return trialResult;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Status = LicenseStatus.NotActivated;
                result.ErrorMessage = $"许可证验证失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 激活许可证
        /// </summary>
        /// <param name="licenseFilePath">许可证文件路径</param>
        public async Task<(bool Success, string Message)> ActivateLicenseAsync(string licenseFilePath)
        {
            try
            {
                if (!File.Exists(licenseFilePath))
                    return (false, "许可证文件不存在");

                // 读取许可证文件
                var json = await File.ReadAllTextAsync(licenseFilePath);
                var license = JsonSerializer.Deserialize<LicenseData>(json);

                if (license == null)
                    return (false, "许可证文件格式错误");

                // 验证许可证
                var validationResult = ValidateLicenseFile(license);
                if (!validationResult.IsValid)
                    return (false, validationResult.ErrorMessage);

                // 保存许可证
                SecureStorage.SaveLicense(license);
                _cachedLicense = license;
                IsActivated = true;

                // 删除试用信息
                SecureStorage.DeleteTrialInfo();
                _cachedTrialInfo = null;

                return (true, "许可证激活成功");
            }
            catch (Exception ex)
            {
                return (false, $"激活失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化试用期
        /// </summary>
        public void InitializeTrial()
        {
            var trialInfo = new TrialInfo
            {
                FirstRunTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TrialDays = TRIAL_DAYS,
                LastRunTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                HardwareId = CurrentHardwareId,
                DetectedTimeRollback = false
            };

            SecureStorage.SaveTrialInfo(trialInfo);
            _cachedTrialInfo = trialInfo;
        }

        /// <summary>
        /// 更新最后运行时间
        /// </summary>
        public void UpdateLastRunTime()
        {
            try
            {
                var trialInfo = SecureStorage.LoadTrialInfo();
                if (trialInfo != null)
                {
                    trialInfo.LastRunTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    SecureStorage.SaveTrialInfo(trialInfo);
                    _cachedTrialInfo = trialInfo;
                }
            }
            catch
            {
                // 忽略更新失败
            }
        }

        /// <summary>
        /// 获取剩余天数
        /// </summary>
        public int GetRemainingDays()
        {
            if (IsActivated && _cachedLicense != null)
            {
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(_cachedLicense.ExpiresAt);
                var remaining = (expiresAt - DateTimeOffset.UtcNow).Days;
                return _cachedLicense.ExpiresAt == 0 ? -1 : remaining; // 0表示永久授权
            }

            if (_cachedTrialInfo != null)
            {
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(_cachedTrialInfo.FirstRunTime + _cachedTrialInfo.TrialDays * 86400);
                return (expiresAt - DateTimeOffset.UtcNow).Days;
            }

            return 0;
        }

        /// <summary>
        /// 导入许可证文件
        /// </summary>
        public async Task<(bool Success, string Message)> ImportLicenseFileAsync(string filePath)
        {
            return await ActivateLicenseAsync(filePath);
        }

        /// <summary>
        /// 导出当前硬件信息
        /// </summary>
        public string ExportHardwareInfo()
        {
            return HardwareInfo.GetMachineInfo();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载缓存数据
        /// </summary>
        private void LoadCachedData()
        {
            _cachedLicense = SecureStorage.LoadLicense();
            _cachedTrialInfo = SecureStorage.LoadTrialInfo();
            IsActivated = _cachedLicense != null;
        }

        /// <summary>
        /// 验证已激活的许可证
        /// </summary>
        private LicenseValidationResult ValidateActivatedLicense(LicenseData license)
        {
            var result = new LicenseValidationResult { License = license };

            // 1. 验证硬件指纹 (仅在启用硬件检查时执行)
#if ENABLE_HARDWARE_CHECK
            if (license.HardwareId != CurrentHardwareId)
            {
                result.IsValid = false;
                result.Status = LicenseStatus.HardwareMismatch;
                result.ErrorMessage = "许可证与当前硬件不匹配";
                return result;
            }
#endif

            // 2. 验证签名
            var dataToSign = $"{license.LicenseId}|{license.CustomerName}|{license.HardwareId}|{license.ProductVersion}|{(int)license.LicenseType}|{license.IssuedAt}|{license.ExpiresAt}|{(int)license.Features}";
            if (!CryptoHelper.VerifyLicenseSignature(dataToSign, license.Signature))
            {
                result.IsValid = false;
                result.Status = LicenseStatus.InvalidSignature;
                result.ErrorMessage = "许可证签名无效";
                return result;
            }

            // 3. 验证过期时间
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(license.ExpiresAt);
            if (license.ExpiresAt != 0 && expiresAt < DateTimeOffset.UtcNow)
            {
                result.IsValid = false;
                result.Status = LicenseStatus.LicenseExpired;
                result.RemainingDays = 0;
                result.ErrorMessage = $"许可证已于 {expiresAt:yyyy-MM-dd} 过期";
                return result;
            }

            // 4. 验证通过
            result.IsValid = true;
            result.Status = LicenseStatus.Activated;
            result.RemainingDays = license.ExpiresAt == 0 ? -1 : (expiresAt - DateTimeOffset.UtcNow).Days;

            return result;
        }

        /// <summary>
        /// 验证试用许可证
        /// </summary>
        private LicenseValidationResult ValidateTrialLicense(TrialInfo? trialInfo)
        {
            var result = new LicenseValidationResult { TrialInfo = trialInfo };

            // 1. 首次运行
            if (trialInfo == null)
            {
                result.IsValid = true;
                result.Status = LicenseStatus.NotActivated;
                result.RemainingDays = TRIAL_DAYS;
                result.ErrorMessage = "欢迎使用！请开始试用或激活许可证";
                return result;
            }

            // 2. 检测时间回退
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (trialInfo.LastRunTime > currentTime)
            {
                trialInfo.DetectedTimeRollback = true;
                SecureStorage.SaveTrialInfo(trialInfo);

                result.IsValid = false;
                result.Status = LicenseStatus.TimeRollbackDetected;
                result.ErrorMessage = "检测到系统时间被回退，试用已失效";
                return result;
            }

            // 3. 检查硬件指纹是否变化 (仅在启用硬件检查时执行)
#if ENABLE_HARDWARE_CHECK
            if (trialInfo.HardwareId != CurrentHardwareId)
            {
                result.IsValid = false;
                result.Status = LicenseStatus.HardwareMismatch;
                result.ErrorMessage = "硬件环境已改变，试用已失效";
                return result;
            }
#endif

            // 4. 计算剩余天数
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(trialInfo.FirstRunTime + trialInfo.TrialDays * 86400);
            var remainingDays = (expiresAt - DateTimeOffset.UtcNow).Days;

            // 5. 检查是否过期
            if (remainingDays <= 0)
            {
                result.IsValid = false;
                result.Status = LicenseStatus.TrialExpired;
                result.RemainingDays = 0;
                result.ErrorMessage = $"试用期已于 {expiresAt:yyyy-MM-dd} 结束，请购买许可证";
                return result;
            }

            // 6. 试用有效
            result.IsValid = true;
            result.Status = LicenseStatus.TrialActive;
            result.RemainingDays = remainingDays;

            return result;
        }

        /// <summary>
        /// 验证许可证文件
        /// </summary>
        private LicenseValidationResult ValidateLicenseFile(LicenseData license)
        {
            var result = new LicenseValidationResult();

            // 1. 验证必填字段
            if (string.IsNullOrEmpty(license.LicenseId) ||
                string.IsNullOrEmpty(license.CustomerName) ||
                string.IsNullOrEmpty(license.HardwareId) ||
                string.IsNullOrEmpty(license.Signature))
            {
                result.ErrorMessage = "许可证文件缺少必要字段";
                return result;
            }

            // 2. 验证硬件指纹 (仅在启用硬件检查时执行)
#if ENABLE_HARDWARE_CHECK
            if (license.HardwareId != CurrentHardwareId)
            {
                result.ErrorMessage = "许可证与当前硬件不匹配";
                return result;
            }
#endif

            // 3. 验证签名
            var dataToSign = $"{license.LicenseId}|{license.CustomerName}|{license.HardwareId}|{license.ProductVersion}|{(int)license.LicenseType}|{license.IssuedAt}|{license.ExpiresAt}|{(int)license.Features}";
            if (!CryptoHelper.VerifyLicenseSignature(dataToSign, license.Signature))
            {
                result.ErrorMessage = "许可证签名无效，可能是伪造的许可证";
                return result;
            }

            // 4. 验证通过
            result.IsValid = true;
            return result;
        }

        #endregion
    }
}
