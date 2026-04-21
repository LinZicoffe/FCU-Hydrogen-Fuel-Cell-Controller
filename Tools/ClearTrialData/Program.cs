using System;
using System.IO;
using System.IO.IsolatedStorage;

namespace ClearTrialData
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  FCU 许可证试用数据清除工具");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                // 获取当前用户的 IsolatedStorage 路径
                var isoStorePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IsolatedStorage"
                );

                Console.WriteLine($"正在清除试用数据...");
                Console.WriteLine($"路径: {isoStorePath}");
                Console.WriteLine();

                if (Directory.Exists(isoStorePath))
                {
                    // 递归删除所有内容
                    Directory.Delete(isoStorePath, recursive: true);
                    Console.WriteLine("✓ 试用数据已成功清除！");
                }
                else
                {
                    Console.WriteLine("✓ 未找到试用数据（无需清除）");
                }

                Console.WriteLine();
                Console.WriteLine("现在可以重新运行程序开始新的试用期。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 清除失败: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("请尝试手动删除以下文件夹：");
                Console.WriteLine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IsolatedStorage"
                ));
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}
