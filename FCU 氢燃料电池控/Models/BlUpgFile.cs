using System;
using System.IO;
using System.Security.Cryptography;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// .upg 升级包解析 (格式沿用 H7 工具 BootPkgHeader: 100B 头 + payload)
    /// 创建日期: 2026-08-19
    /// 依据: docs/03-设计方案/FCU-RS485-Bootloader在线升级设计方案.md §5
    /// </summary>
    public class BlUpgFile
    {
        public const uint BOOT_PKG_MAGIC = 0x55465731;   // 'UFW1'
        public const int HEADER_SIZE = 100;

        public uint Magic { get; private set; }
        public uint HdrVersion { get; private set; }
        public uint HdrSize { get; private set; }
        public uint Flags { get; private set; }
        public byte[] TargetUid { get; private set; } = new byte[12];
        public uint FwSize { get; private set; }
        public uint FwVersion { get; private set; }
        public byte[] FwSha256 { get; private set; } = new byte[32];

        /// <summary>固件 payload (APP 区原始镜像, 链接基址 0x08004000)</summary>
        public byte[] Payload { get; private set; } = Array.Empty<byte>();

        /// <summary>payload 的 CRC32 (CRC-32/BZIP2 参数组, 与 BL Verify 一致)</summary>
        public uint PayloadCrc32 { get; private set; }

        /// <summary>TargetUid 是否为全 0xFF (批量产线包, 不限设备)</summary>
        public bool IsUidWildcard => TargetUid.All(b => b == 0xFF);

        private BlUpgFile() { }

        /// <summary>
        /// 解析并校验 .upg 文件
        /// </summary>
        /// <param name="path">.upg 文件路径</param>
        /// <param name="error">失败原因</param>
        public static BlUpgFile? TryLoad(string path, out string error)
        {
            error = string.Empty;

            try
            {
                if (!File.Exists(path))
                {
                    error = "文件不存在";
                    return null;
                }

                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length < HEADER_SIZE)
                {
                    error = $"文件过短({raw.Length}B), 少于 100B 升级包头";
                    return null;
                }

                var pkg = new BlUpgFile
                {
                    Magic = ReadU32(raw, 0),
                    HdrVersion = ReadU32(raw, 4),
                    HdrSize = ReadU32(raw, 8),
                    Flags = ReadU32(raw, 12),
                };
                Array.Copy(raw, 16, pkg.TargetUid, 0, 12);
                pkg.FwSize = ReadU32(raw, 28);
                pkg.FwVersion = ReadU32(raw, 32);
                Array.Copy(raw, 36, pkg.FwSha256, 0, 32);

                if (pkg.Magic != BOOT_PKG_MAGIC)
                {
                    error = $"升级包魔数错误(0x{pkg.Magic:X8}), 不是 .upg 文件";
                    return null;
                }
                if (pkg.HdrSize != HEADER_SIZE)
                {
                    error = $"包头长度异常({pkg.HdrSize})";
                    return null;
                }
                if (pkg.FwSize == 0 || pkg.FwSize > Protocol.BlProtocolHelper.APP_SIZE_MAX)
                {
                    error = $"固件大小 {pkg.FwSize}B 超出 APP 分区上限 {Protocol.BlProtocolHelper.APP_SIZE_MAX}B";
                    return null;
                }
                if (raw.Length < HEADER_SIZE + (int)pkg.FwSize)
                {
                    error = $"文件不完整: 声明 {pkg.FwSize}B payload, 实际仅 {raw.Length - HEADER_SIZE}B";
                    return null;
                }

                pkg.Payload = new byte[pkg.FwSize];
                Array.Copy(raw, HEADER_SIZE, pkg.Payload, 0, (int)pkg.FwSize);

                // 本地双层校验第一层: SHA256 防文件损坏/选错文件 (第二层为 BL Verify CRC32)
                byte[] sha = SHA256.HashData(pkg.Payload);
                if (!sha.SequenceEqual(pkg.FwSha256))
                {
                    error = "payload SHA256 校验失败, 升级包文件已损坏";
                    return null;
                }

                pkg.PayloadCrc32 = Protocol.BlProtocolHelper.Crc32Mpeg2(pkg.Payload, pkg.Payload.Length);
                return pkg;
            }
            catch (Exception ex)
            {
                error = $"读取升级包异常: {ex.Message}";
                return null;
            }
        }

        /// <summary>校验目标设备 UID 是否匹配 (全 0xFF 通配)</summary>
        public bool IsUidMatch(byte[] deviceUid)
        {
            if (IsUidWildcard) return true;
            if (deviceUid == null || deviceUid.Length != 12) return false;
            return TargetUid.SequenceEqual(deviceUid);
        }

        /// <summary>构建 WriteMeta 命令数据域: magic/size/crc32/version 各 4B 小端 (BL 端补 meta_crc32)</summary>
        public byte[] BuildWriteMetaData()
        {
            var data = new byte[16];
            WriteU32(data, 0, Protocol.BlProtocolHelper.META_MAGIC);
            WriteU32(data, 4, FwSize);
            WriteU32(data, 8, PayloadCrc32);
            WriteU32(data, 12, FwVersion);
            return data;
        }

        private static uint ReadU32(byte[] buf, int off)
        {
            return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
        }

        private static void WriteU32(byte[] buf, int off, uint v)
        {
            buf[off] = (byte)(v & 0xFF);
            buf[off + 1] = (byte)((v >> 8) & 0xFF);
            buf[off + 2] = (byte)((v >> 16) & 0xFF);
            buf[off + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
