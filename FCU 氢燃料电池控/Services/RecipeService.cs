using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FCU_氢燃料电池控.Models;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>
    /// 拉载配方管理服务
    /// 管理系统预设配方和用户自建配方的CRUD操作及JSON持久化
    /// </summary>
    public class RecipeService
    {
        private readonly string _recipeFilePath;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 系统预设配方（运行时生成，不可持久化修改）
        /// </summary>
        private readonly List<LoadRecipe> _systemPresets;

        /// <summary>
        /// 用户自建配方
        /// </summary>
        private List<LoadRecipe> _userRecipes = new();

        public RecipeService()
        {
            _recipeFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes.json");
            _systemPresets = CreateSystemPresets();
            LoadUserRecipes();
        }

        /// <summary>
        /// 获取所有配方（系统预设 + 用户配方）
        /// </summary>
        public List<LoadRecipe> GetAllRecipes()
        {
            var all = new List<LoadRecipe>(_systemPresets);
            all.AddRange(_userRecipes);
            return all;
        }

        /// <summary>
        /// 获取系统预设配方
        /// </summary>
        public IReadOnlyList<LoadRecipe> GetSystemPresets() => _systemPresets.AsReadOnly();

        /// <summary>
        /// 获取用户自建配方
        /// </summary>
        public IReadOnlyList<LoadRecipe> GetUserRecipes() => _userRecipes.AsReadOnly();

        /// <summary>
        /// 根据ID查找配方
        /// </summary>
        public LoadRecipe? GetRecipeById(string id)
        {
            foreach (var recipe in _systemPresets)
            {
                if (recipe.Id == id) return recipe;
            }
            foreach (var recipe in _userRecipes)
            {
                if (recipe.Id == id) return recipe;
            }
            return null;
        }

        /// <summary>
        /// 新建用户配方
        /// </summary>
        public (bool Success, string Message, LoadRecipe? Recipe) AddRecipe(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return (false, "配方名称不能为空", null);

            var recipe = new LoadRecipe
            {
                Name = name.Trim(),
                IsSystemPreset = false
            };

            _userRecipes.Add(recipe);
            SaveUserRecipes();
            return (true, "配方创建成功", recipe);
        }

        /// <summary>
        /// 复制配方（生成用户配方副本）
        /// </summary>
        public (bool Success, string Message, LoadRecipe? Recipe) CopyRecipe(string sourceId, string? newName = null)
        {
            var source = GetRecipeById(sourceId);
            if (source == null)
                return (false, "未找到源配方", null);

            var copy = source.Clone();
            copy.Name = newName ?? $"{source.Name} (副本)";

            _userRecipes.Add(copy);
            SaveUserRecipes();
            return (true, "配方复制成功", copy);
        }

        /// <summary>
        /// 重命名用户配方
        /// </summary>
        public (bool Success, string Message) RenameRecipe(string id, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return (false, "配方名称不能为空");

            var recipe = _userRecipes.Find(r => r.Id == id);
            if (recipe == null)
                return (false, "未找到该用户配方");

            recipe.Name = newName.Trim();
            recipe.ModifiedAt = DateTime.Now;
            SaveUserRecipes();
            return (true, "重命名成功");
        }

        /// <summary>
        /// 删除用户配方（系统预设不可删除）
        /// </summary>
        public (bool Success, string Message) DeleteRecipe(string id)
        {
            var index = _userRecipes.FindIndex(r => r.Id == id);
            if (index < 0)
                return (false, "未找到该用户配方");

            _userRecipes.RemoveAt(index);
            SaveUserRecipes();
            return (true, "配方已删除");
        }

        /// <summary>
        /// 保存用户配方（更新参数值）
        /// </summary>
        public (bool Success, string Message) SaveRecipe(string id, ushort[] currents, ushort[] times)
        {
            var recipe = _userRecipes.Find(r => r.Id == id);
            if (recipe == null)
                return (false, "未找到该用户配方");

            if (currents.Length != 15 || times.Length != 15)
                return (false, "参数数据长度不正确");

            Array.Copy(currents, recipe.Currents, 15);
            Array.Copy(times, recipe.Times, 15);
            recipe.ModifiedAt = DateTime.Now;
            SaveUserRecipes();
            return (true, "配方已保存");
        }

        /// <summary>
        /// 从磁盘加载用户配方
        /// </summary>
        private void LoadUserRecipes()
        {
            try
            {
                if (!File.Exists(_recipeFilePath))
                {
                    _userRecipes = new List<LoadRecipe>();
                    return;
                }

                var json = File.ReadAllText(_recipeFilePath);
                var collection = JsonSerializer.Deserialize<RecipeCollection>(json, _jsonOptions);
                _userRecipes = collection?.UserRecipes ?? new List<LoadRecipe>();
            }
            catch
            {
                // 加载失败时使用空列表
                _userRecipes = new List<LoadRecipe>();
            }
        }

        /// <summary>
        /// 保存用户配方到磁盘
        /// </summary>
        private void SaveUserRecipes()
        {
            try
            {
                var collection = new RecipeCollection { UserRecipes = _userRecipes };
                var json = JsonSerializer.Serialize(collection, _jsonOptions);
                File.WriteAllText(_recipeFilePath, json);
            }
            catch
            {
                // 保存失败静默处理
            }
        }

        /// <summary>
        /// 创建系统预设配方
        /// </summary>
        private static List<LoadRecipe> CreateSystemPresets()
        {
            return new List<LoadRecipe>
            {
                new LoadRecipe
                {
                    Id = "preset-low-current",
                    Name = "低电流测试",
                    IsSystemPreset = true,
                    Currents = new ushort[]{ 5, 10, 20, 30, 40, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50 },
                    Times = new ushort[]{ 5, 5, 5, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 }
                },
                new LoadRecipe
                {
                    Id = "preset-mid-current",
                    Name = "中电流测试",
                    IsSystemPreset = true,
                    Currents = new ushort[]{ 5, 10, 20, 40, 70, 110, 150, 210, 280, 340, 360, 380, 360, 340, 1000 },
                    Times = new ushort[]{ 5, 5, 5, 5, 5, 7, 7, 10, 10, 10, 10, 2, 5, 5, 5 }
                },
                new LoadRecipe
                {
                    Id = "preset-high-current",
                    Name = "高电流测试",
                    IsSystemPreset = true,
                    Currents = new ushort[]{ 50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1000, 800, 600, 1000 },
                    Times = new ushort[]{ 10, 10, 10, 15, 15, 15, 20, 20, 20, 20, 10, 5, 10, 10, 10 }
                },
                new LoadRecipe
                {
                    Id = "preset-staircase",
                    Name = "阶梯测试",
                    IsSystemPreset = true,
                    Currents = new ushort[]{ 100, 100, 100, 250, 250, 250, 400, 400, 400, 550, 550, 550, 700, 700, 700 },
                    Times = new ushort[]{ 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
                },
                new LoadRecipe
                {
                    Id = "preset-quick",
                    Name = "快速测试",
                    IsSystemPreset = true,
                    Currents = new ushort[]{ 50, 200, 400, 600, 800, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    Times = new ushort[]{ 3, 3, 5, 5, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                }
            };
        }
    }
}
