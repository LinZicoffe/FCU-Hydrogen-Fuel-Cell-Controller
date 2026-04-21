using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Management;

namespace FCU_氢燃料电池控.Utils
{
    /// <summary>
    /// 硬件信息获取工具类
    /// </summary>
    public static class HardwareInfo
    {
        /// <summary>
        /// 获取硬件指纹 (SHA256哈希)
        /// 算法: SHA256(CPU_ID + Motherboard_Serial + MAC_Address + Disk_Serial)
        /// </summary>
        /// <returns>硬件指纹</returns>
        public static string GetHardwareFingerprint()
        {
            try
            {
                var components = new StringBuilder();

                // 1. CPU ID
                components.Append(GetCpuId());

                // 2. 主板序列号
                components.Append(GetMotherboardSerial());

                // 3. MAC地址 (取第一个有效的网络适配器)
                components.Append(GetMacAddress());

                // 4. 硬盘序列号 (系统盘)
                components.Append(GetDiskSerial());

                // 计算SHA256哈希
                using var sha256 = SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));

                // 转换为十六进制字符串
                return Convert.ToHexString(hashBytes).ToLower();
            }
            catch
            {
                // 降级方案: 使用MachineName和UserName组合
                var fallback = Environment.MachineName + Environment.UserName;
                using var sha256 = SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallback));
                return Convert.ToHexString(hashBytes).ToLower();
            }
        }

        /// <summary>
        /// 获取CPU处理器ID
        /// </summary>
        private static string GetCpuId()
        {
            try
            {
                // 使用WMI获取CPU信息
                var cpuInfo = string.Empty;
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    cpuInfo = obj["ProcessorId"]?.ToString() ?? string.Empty;
                    break;
                }
                return cpuInfo;
            }
            catch
            {
                return "UNKNOWN_CPU";
            }
        }

        /// <summary>
        /// 获取主板序列号
        /// </summary>
        private static string GetMotherboardSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (var obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString();
                    if (!string.IsNullOrEmpty(serial))
                        return serial;
                }
                return "UNKNOWN_MOTHERBOARD";
            }
            catch
            {
                return "UNKNOWN_MOTHERBOARD";
            }
        }

        /// <summary>
        /// 获取MAC地址
        /// </summary>
        private static string GetMacAddress()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n =>
                        n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase));

                return nic?.GetPhysicalAddress().ToString() ?? "UNKNOWN_MAC";
            }
            catch
            {
                return "UNKNOWN_MAC";
            }
        }

        /// <summary>
        /// 获取硬盘序列号
        /// </summary>
        private static string GetDiskSerial()
        {
            try
            {
                // 获取系统盘（通常是 C 盘）
                var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var driveLetter = systemRoot.Substring(0, 2);

                using var searcher = new ManagementObjectSearcher($"SELECT SerialNumber FROM Win32_LogicalDisk WHERE DeviceID='{driveLetter}'");
                foreach (var obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString();
                    if (!string.IsNullOrEmpty(serial))
                        return serial;
                }
                return "UNKNOWN_DISK";
            }
            catch
            {
                return "UNKNOWN_DISK";
            }
        }

        /// <summary>
        /// 获取简化的硬件信息摘要 (用于显示)
        /// </summary>
        public static string GetHardwareSummary()
        {
            try
            {
                var cpu = GetCpuId();
                var mac = GetMacAddress();
                return $"CPU: {cpu[..8]}... | MAC: {mac}";
            }
            catch
            {
                return "硬件信息获取失败";
            }
        }

        /// <summary>
        /// 获取机器信息（用于生成许可证申请）
        /// </summary>
        /// <returns>机器信息字符串</returns>
        public static string GetMachineInfo()
        {
            try
            {
                var hardwareId = GetHardwareFingerprint();
                var machineName = Environment.MachineName;
                var userName = Environment.UserName;
                var osVersion = Environment.OSVersion.ToString();
                var currentTime = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

                return $"机器名: {machineName}\n" +
                       $"用户名: {userName}\n" +
                       $"操作系统: {osVersion}\n" +
                       $"硬件指纹: {hardwareId}\n" +
                       $"生成时间: {currentTime}";
            }
            catch (Exception ex)
            {
                return $"获取机器信息失败: {ex.Message}";
            }
        }
    }
}
