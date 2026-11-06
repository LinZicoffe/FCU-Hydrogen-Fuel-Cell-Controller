using System.IO;
using System.Reflection;
using FCU_氢燃料电池控.Services;

internal static class RecipeChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fcu-recipe-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "recipes.json");
        var pathField = typeof(RecipeService).GetField("_recipeFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        RecipeService Open()
        {
            var service = new RecipeService();
            pathField.SetValue(service, path);
            typeof(RecipeService).GetMethod("LoadUserRecipes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            return service;
        }
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            try { Program.Check(condition, message); }
            catch (Exception ex) { failures.Add(ex.Message); }
        }
        try
        {
            var service = Open();
            Check(service.GetSystemPresets().Count > 0 && service.GetSystemPresets().All(p => p.Currents.All(c => c <= 500)),
                "every offered system preset fits the firmware50A limit");
            var add = service.AddRecipe("原配方");
            Program.Check(add.Success && add.Recipe != null, "recipe creation persists successfully");
            string id = add.Recipe!.Id;
            var currents = Enumerable.Repeat((ushort)10, 15).ToArray();
            var times = Enumerable.Repeat((ushort)5, 15).ToArray();
            Program.Check(service.SaveRecipe(id, currents, times).Success, "recipe parameters persist successfully");
            Program.Check(Open().GetRecipeById(id)!.Currents.SequenceEqual(currents), "new service reloads saved recipe values");

            // 独占旧文件，稳定触发写入/替换失败；真实文件必须保持完整。
            string before = File.ReadAllText(path);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failedAdd = service.AddRecipe("不应保存");
                Check(!failedAdd.Success && failedAdd.Message.Contains("保存") && service.GetUserRecipes().Count == 1,
                    "failed add reports error and preserves in-memory collection");
                var failedCopy = service.CopyRecipe(id);
                Check(!failedCopy.Success && service.GetUserRecipes().Count == 1,
                    "failed copy does not remain in memory");
                var failedRename = service.RenameRecipe(id, "不应改名");
                Check(!failedRename.Success && service.GetRecipeById(id)!.Name == "原配方",
                    "failed rename preserves original name");
                var failedSave = service.SaveRecipe(id, Enumerable.Repeat((ushort)20, 15).ToArray(), times);
                Check(!failedSave.Success && service.GetRecipeById(id)!.Currents.SequenceEqual(currents),
                    "failed save preserves original current values");
                var failedDelete = service.DeleteRecipe(id);
                Check(!failedDelete.Success && service.GetRecipeById(id) != null,
                    "failed delete preserves recipe in memory");
            }
            Check(File.ReadAllText(path) == before, "all failed writes preserve the original recipe file");
            Check(Directory.GetFiles(root).Length == 1, "failed atomic writes leave no temporary files");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
            Program.Check(service.RenameRecipe(id, "改名后").Success && Open().GetRecipeById(id)!.Name == "改名后", "successful rename survives reload");
            Program.Check(service.DeleteRecipe(id).Success && Open().GetUserRecipes().Count == 0, "successful delete survives reload");
        }
        finally
        {
            // 只清理本测试生成的直接子文件；不递归删除计算得到的目录。
            foreach (string generated in Directory.GetFiles(root)) File.Delete(generated);
            Directory.Delete(root);
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}
