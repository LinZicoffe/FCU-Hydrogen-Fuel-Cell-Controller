using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// OpenBLT 升级用 S-record(Motorola S1/S2/S3) 固件文件解析与校验
    /// 创建日期: 2026-09-21
    /// 依据: docs/实施步骤-OpenBLT升级与固件适配.md §4.2
    ///   - 可擦写区 [0x08003000, 0x0801F000): Boot-For-FCU Source/ARMCM4_STM32G4/flash_layout.inc (28x4K, 参数页 0x0801F000 排除)
    ///   - 首地址=0x08003000: codex/openblt-app-upgrade 分支 STM32G474CBTX_FLASH.ld (ORIGIN=0x08003000, LENGTH=112K)
    ///   - 偏移 0x1D8 处占位字 0x55AA11EE(小端 EE 11 AA 55): 启动文件 startup_stm32g474cbtx.s:252 + Boot-For-FCU flash.c:59(BOOT_FLASH_VECTOR_TABLE_CS_OFFSET=0x1D8)
    /// </summary>
    public class SrecFile
    {
        public const uint APP_BASE = 0x08003000;            /* APP 链接基址(向量表起点) */
        public const uint APP_END_EXCLUSIVE = 0x0801F000;   /* 参数页起始, 不可触碰 */
        public const uint APP_MAX_SIZE = 0x1C000;           /* 112K */
        public const int CHECKSUM_OFFSET = 0x1D8;           /* BL 校验字在 APP 镜像内偏移 */

        /// <summary>校验字占位 0x55AA11EE 的存储序(ARM 小端: 低位字节在前)</summary>
        private static readonly byte[] ChecksumPlaceholderLE = { 0xEE, 0x11, 0xAA, 0x55 };

        /// <summary>镜像基地址(恒为 0x08003000, 解析保证)</summary>
        public uint BaseAddress => APP_BASE;

        /// <summary>合并后的连续 APP 镜像(间隙以 0xFF 填充)</summary>
        public byte[] Image { get; private set; } = Array.Empty<byte>();

        /// <summary>镜像长度(字节)</summary>
        public int Length => Image.Length;

        /// <summary>解析过程中的非致命警告(间隙填充等)</summary>
        public List<string> Warnings { get; } = new List<string>();

        private SrecFile() { }

        /// <summary>
        /// 解析并校验 S-record 文件(.srec/.s19/.mot)
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="error">失败原因(供 UI 直接展示)</param>
        public static SrecFile? TryLoad(string path, out string error)
        {
            error = string.Empty;

            if (!File.Exists(path))
            {
                error = "文件不存在";
                return null;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                error = $"读取文件失败: {ex.Message}";
                return null;
            }

            /* 第一遍: 逐行解析 S 记录为 (地址, 数据) 片段, 校验行内 checksum */
            var segments = new List<(uint Address, byte[] Data)>();
            int dataRecordCount = 0;

            for (int lineNo = 0; lineNo < lines.Length; lineNo++)
            {
                string line = lines[lineNo].Trim();
                if (line.Length == 0)
                    continue;

                if (line[0] != 'S' || line.Length < 4 || (line.Length - 2) % 2 != 0)
                {
                    error = $"第 {lineNo + 1} 行不是合法 S-record 行";
                    return null;
                }

                int typeChar = line[1];
                byte[] bytes = ParseHexBytes(line, 2, line.Length - 2);
                if (bytes.Length == 0)
                {
                    error = $"第 {lineNo + 1} 行含非法十六进制字符";
                    return null;
                }

                /* bytes[0]=byte count(其后地址+数据+checksum 的字节数), 最后 1 字节为行 checksum */
                if (bytes.Length - 1 != bytes[0])
                {
                    error = $"第 {lineNo + 1} 行 byte count({bytes[0]}) 与实际长度({bytes.Length - 1})不符";
                    return null;
                }

                /* checksum: count+地址+数据 之和(含 checksum 字节)按 256 取余应恒为 0xFF */
                int sum = 0;
                for (int i = 0; i < bytes.Length; i++)
                    sum += bytes[i];
                if ((sum & 0xFF) != 0xFF)
                {
                    error = $"第 {lineNo + 1} 行 checksum 校验失败";
                    return null;
                }

                switch (typeChar)
                {
                    case '0':          /* 头记录, 忽略 */
                    case '5':          /* 记录数, 忽略 */
                    case '7':          /* S3 结束记录, 忽略 */
                    case '8':          /* S2 结束记录, 忽略 */
                    case '9':          /* S1 结束记录, 忽略 */
                        break;

                    case '1':
                    case '2':
                    case '3':
                    {
                        int addrLen = (typeChar - '0') + 1;    /* S1=2B, S2=3B, S3=4B 地址(类型号+1) */
                        int dataLen = bytes[0] - addrLen - 1;   /* 减地址与 checksum */
                        if (dataLen < 0)
                        {
                            error = $"第 {lineNo + 1} 行长度不足以容纳地址字段";
                            return null;
                        }
                        if (dataLen == 0)
                            break;                      /* 零长度数据记录, 忽略 */

                        uint address = 0;
                        for (int i = 1; i <= addrLen; i++)
                            address = (address << 8) | bytes[i];

                        byte[] data = new byte[dataLen];
                        Array.Copy(bytes, 1 + addrLen, data, 0, dataLen);

                        segments.Add((address, data));
                        dataRecordCount++;
                        break;
                    }

                    default:
                        error = $"第 {lineNo + 1} 行记录类型 S{typeChar} 不受支持";
                        return null;
                }
            }

            if (dataRecordCount == 0 || segments.Count == 0)
            {
                error = "文件中无 S1/S2/S3 数据记录";
                return null;
            }

            /* 第二遍: 排序合并为连续镜像, 校验地址窗/首地址/重叠 */
            segments.Sort((a, b) => a.Address.CompareTo(b.Address));

            if (segments[0].Address != APP_BASE)
            {
                error = $"首记录地址 0x{segments[0].Address:X8} 不等于 APP 基址 0x{APP_BASE:X8}, 不是 OpenBLT 适配固件的构建产物";
                return null;
            }

            for (int i = 1; i < segments.Count; i++)
            {
                uint prevEnd = segments[i - 1].Address + (uint)segments[i - 1].Data.Length;
                if (segments[i].Address < prevEnd)
                {
                    error = $"地址重叠: 0x{segments[i].Address:X8} 落在上一片段 [0x{segments[i - 1].Address:X8}, 0x{prevEnd:X8}) 内";
                    return null;
                }
            }

            uint lastEnd = segments[^1].Address + (uint)segments[^1].Data.Length;
            if (lastEnd < APP_BASE)
            {
                /* 地址求和环绕(如恶意地址 ≥0xFFFFxxxx)，避免后续差值显示无意义大数 */
                error = "地址运算溢出，片段地址非法";
                return null;
            }
            if (lastEnd > APP_END_EXCLUSIVE)
            {
                error = $"数据越界: 末地址 0x{lastEnd:X8} 超出 APP 可写区上限 0x{APP_END_EXCLUSIVE:X8}(0x0801F000 起为参数页)";
                return null;
            }
            if (lastEnd - APP_BASE > APP_MAX_SIZE)
            {
                error = $"镜像长度 {lastEnd - APP_BASE}(0x{lastEnd - APP_BASE:X}) 超过 APP 区上限 {APP_MAX_SIZE}";
                return null;
            }

            var file = new SrecFile();
            int span = (int)(lastEnd - APP_BASE);
            var image = new byte[span];
            Array.Fill(image, (byte)0xFF);

            for (int i = 0; i < segments.Count; i++)
            {
                int offset = (int)(segments[i].Address - APP_BASE);
                Array.Copy(segments[i].Data, 0, image, offset, segments[i].Data.Length);

                if (i > 0)
                {
                    uint prevEnd = segments[i - 1].Address + (uint)segments[i - 1].Data.Length;
                    if (segments[i].Address > prevEnd)
                        file.Warnings.Add($"片段间隙 [0x{prevEnd:X8}, 0x{segments[i].Address:X8}) 以 0xFF 填充");
                }
            }

            /* 第三遍: 校验字占位强校验(拦截错误基址/错误分支的构建产物) */
            if (span < CHECKSUM_OFFSET + 4)
            {
                error = $"镜像长度 {span} 未覆盖校验字偏移 0x{CHECKSUM_OFFSET:X}, 不是完整的 APP 向量表";
                return null;
            }
            for (int i = 0; i < 4; i++)
            {
                if (image[CHECKSUM_OFFSET + i] != ChecksumPlaceholderLE[i])
                {
                    error = $"镜像偏移 0x{CHECKSUM_OFFSET:X} 处不是 OpenBLT 校验字占位 0x55AA11EE(实测 0x{image[CHECKSUM_OFFSET + 3]:X2}{image[CHECKSUM_OFFSET + 2]:X2}{image[CHECKSUM_OFFSET + 1]:X2}{image[CHECKSUM_OFFSET]:X2}), 拒绝烧录";
                    return null;
                }
            }

            file.Image = image;
            return file;
        }

        private static byte[] ParseHexBytes(string line, int start, int length)
        {
            var result = new byte[length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexValue(line[start + i * 2]);
                int lo = HexValue(line[start + i * 2 + 1]);
                if (hi < 0 || lo < 0)
                    return Array.Empty<byte>();
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1
        };
    }
}
