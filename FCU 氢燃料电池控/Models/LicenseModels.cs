using System;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// 许可证数据模型
    /// </summary>
    public class LicenseData
    {
        /// <summary>
        /// 许可证唯一标识符 (GUID)
        /// </summary>
        public string LicenseId { get; set; } = string.Empty;

        /// <summary>
        /// 客户名称
        /// </summary>
        public string CustomerName { get; set; } = string.Empty;

        /// <summary>
        /// 硬件指纹 (SHA256哈希)
        /// </summary>
        public string HardwareId { get; set; } = string.Empty;

        /// <summary>
        /// 产品版本
        /// </summary>
        public string ProductVersion { get; set; } = "1.0.0";

        /// <summary>
        /// 许可证类型
        /// </summary>
        public LicenseType LicenseType { get; set; } = LicenseType.Standard;

        /// <summary>
        /// 许可证颁发时间 (UTC时间戳)
        /// </summary>
        public long IssuedAt { get; set; }

        /// <summary>
        /// 许可证过期时间 (UTC时间戳, 0表示永久授权)
        /// </summary>
        public long ExpiresAt { get; set; }

        /// <summary>
        /// 功能权限标志位
        /// </summary>
        public LicenseFeatures Features { get; set; } = LicenseFeatures.Basic;

        /// <summary>
        /// RSA签名 (Base64编码)
        /// </summary>
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>
    /// 许可证类型枚举
    /// </summary>
    public enum LicenseType
    {
        /// <summary>
        /// 试用版
        /// </summary>
        Trial = 0,

        /// <summary>
        /// 标准版
        /// </summary>
        Standard = 1,

        /// <summary>
        /// 专业版
        /// </summary>
        Professional = 2,

        /// <summary>
        /// 企业版
        /// </summary>
        Enterprise = 3
    }

    /// <summary>
    /// 许可证功能标志位
    /// </summary>
    [Flags]
    public enum LicenseFeatures
    {
        /// <summary>
        /// 基础功能
        /// </summary>
        Basic = 0x01,

        /// <summary>
        /// 高级配置
        /// </summary>
        AdvancedConfig = 0x02,

        /// <summary>
        /// 数据记录
        /// </summary>
        DataLogging = 0x04,

        /// <summary>
        /// 远程控制
        /// </summary>
        RemoteControl = 0x08,

        /// <summary>
        /// 多设备支持
        /// </summary>
        MultiDevice = 0x10,

        /// <summary>
        /// 自定义脚本
        /// </summary>
        CustomScript = 0x20,

        /// <summary>
        /// 所有功能
        /// </summary>
        AllFeatures = 0xFF
    }

    /// <summary>
    /// 试用信息模型
    /// </summary>
    public class TrialInfo
    {
        /// <summary>
        /// 首次运行时间 (UTC时间戳)
        /// </summary>
        public long FirstRunTime { get; set; }

        /// <summary>
        /// 试用天数 (默认90天)
        /// </summary>
        public int TrialDays { get; set; } = 90;

        /// <summary>
        /// 上次运行时间 (用于检测时间回退)
        /// </summary>
        public long LastRunTime { get; set; }

        /// <summary>
        /// 时间回退检测标志
        /// </summary>
        public bool DetectedTimeRollback { get; set; }

        /// <summary>
        /// 硬件指纹
        /// </summary>
        public string HardwareId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 许可证验证结果
    /// </summary>
    public class LicenseValidationResult
    {
        /// <summary>
        /// 验证是否成功
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 许可证状态
        /// </summary>
        public LicenseStatus Status { get; set; }

        /// <summary>
        /// 剩余天数 (-1表示永久授权)
        /// </summary>
        public int RemainingDays { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 许可证数据 (验证成功时填充)
        /// </summary>
        public LicenseData? License { get; set; }

        /// <summary>
        /// 试用信息 (试用状态下填充)
        /// </summary>
        public TrialInfo? TrialInfo { get; set; }
    }

    /// <summary>
    /// 许可证状态枚举
    /// </summary>
    public enum LicenseStatus
    {
        /// <summary>
        /// 未激活
        /// </summary>
        NotActivated,

        /// <summary>
        /// 试用中
        /// </summary>
        TrialActive,

        /// <summary>
        /// 试用过期
        /// </summary>
        TrialExpired,

        /// <summary>
        /// 已激活
        /// </summary>
        Activated,

        /// <summary>
        /// 许可证过期
        /// </summary>
        LicenseExpired,

        /// <summary>
        /// 硬件不匹配
        /// </summary>
        HardwareMismatch,

        /// <summary>
        /// 签名无效
        /// </summary>
        InvalidSignature,

        /// <summary>
        /// 时间回退检测
        /// </summary>
        TimeRollbackDetected
    }
}
