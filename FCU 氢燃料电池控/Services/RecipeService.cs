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
            if (!SaveUserRecipes(out string error))
            {
                _userRecipes.Remove(recipe);
                return (false, error, null);
            }
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
            if (!SaveUserRecipes(out string error))
            {
                _userRecipes.Remove(copy);
                return (false, error, null);
            }
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

            string oldName = recipe.Name;
            DateTime oldModifiedAt = recipe.ModifiedAt;
            recipe.Name = newName.Trim();
            recipe.ModifiedAt = DateTime.Now;
            if (!SaveUserRecipes(out string error))
            {
                recipe.Name = oldName;
                recipe.ModifiedAt = oldModifiedAt;
                return (false, error);
            }
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

            var recipe = _userRecipes[index];
            _userRecipes.RemoveAt(index);
            if (!SaveUserRecipes(out string error))
            {
                _userRecipes.Insert(index, recipe);
                return (false, error);
            }
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

            ushort[] oldCurrents = (ushort[])recipe.Currents.Clone();
            ushort[] oldTimes = (ushort[])recipe.Times.Clone();
            DateTime oldModifiedAt = recipe.ModifiedAt;
            Array.Copy(currents, recipe.Currents, 15);
            Array.Copy(times, recipe.Times, 15);
            recipe.ModifiedAt = DateTime.Now;
            if (!SaveUserRecipes(out string error))
            {
                Array.Copy(oldCurrents, recipe.Currents, 15);
                Array.Copy(oldTimes, recipe.Times, 15);
                recipe.ModifiedAt = oldModifiedAt;
                return (false, error);
            }
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
        private bool SaveUserRecipes(out string error)
        {
            string? temporaryFile = null;
            try
            {
                var collection = new RecipeCollection { UserRecipes = _userRecipes };
                var json = JsonSerializer.Serialize(collection, _jsonOptions);
                string directory = Path.GetDirectoryName(Path.GetFullPath(_recipeFilePath))!;
                temporaryFile = Path.Combine(directory, $".recipes-{Guid.NewGuid():N}.tmp");
                // 先写同目录临时文件，再替换目标，写入失败不能截断旧配方。
                File.WriteAllText(temporaryFile, json);
                File.Move(temporaryFile, _recipeFilePath, overwrite: true);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = $"配方未保存：{ex.Message}";
                return false;
            }
            finally
            {
                if (temporaryFile != null && File.Exists(temporaryFile))
                {
                    try { File.Delete(temporaryFile); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
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
                }
            };
        }
    }
}
