using System.IO;
using FCU_氢燃料电池控.Models;

internal static class SrecChecks
{
    private static string S3(uint address, byte[] data)
    {
        var bytes = new List<byte> { (byte)(data.Length + 5), (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address };
        bytes.AddRange(data);
        bytes.Add(unchecked((byte)~bytes.Sum(b => b)));
        return "S3" + Convert.ToHexString(bytes.ToArray());
    }

    public static void Run()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "srec-guard-check.srec");
        try
        {
            byte[] image = new byte[0x1DC];
            image[0x1D8] = 0xEE; image[0x1D9] = 0x11; image[0x1DA] = 0xAA; image[0x1DB] = 0x55;
            void Save(uint address)
            {
                var lines = new List<string>();
                for (int offset = 0; offset < image.Length; offset += 32)
                    lines.Add(S3(address + (uint)offset, image[offset..Math.Min(image.Length, offset + 32)]));
                File.WriteAllLines(path, lines);
            }
            Save(0x08000000);
            Program.Check(SrecFile.TryLoad(path, out string error) == null && error.Contains("基址"), "standalone main image rejected by real Srec parser");
            Save(0x08003000);
            Program.Check(SrecFile.TryLoad(path, out _) != null, "matching OpenBLT image still accepted");
            image[0x1D8] = 0; Save(0x08003000);
            Program.Check(SrecFile.TryLoad(path, out error) == null && error.Contains("占位"), "OpenBLT checksum placeholder still enforced");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
