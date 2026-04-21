using System;
using System.Collections.Generic;

namespace FCU_氢燃料电池控.Models
{
    /// <summary>
    /// 拉载配方数据模型
    /// </summary>
    public class LoadRecipe
    {
        /// <summary>
        /// 配方唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 配方名称
        /// </summary>
        public string Name { get; set; } = "新配方";

        /// <summary>
        /// 是否为系统预设（不可删除/重命名）
        /// </summary>
        public bool IsSystemPreset { get; set; } = false;

        /// <summary>
        /// 时序总数（固定15）
        /// </summary>
        public byte SequenceCount { get; set; } = 15;

        /// <summary>
        /// 各时序电流值 (0.1A)
        /// </summary>
        public ushort[] Currents { get; set; } = new ushort[15];

        /// <summary>
        /// 各时序持续时间 (0.1s)
        /// </summary>
        public ushort[] Times { get; set; } = new ushort[15];

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 深拷贝当前配方
        /// </summary>
        public LoadRecipe Clone()
        {
            return new LoadRecipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = this.Name,
                IsSystemPreset = false,
                SequenceCount = this.SequenceCount,
                Currents = (ushort[])this.Currents.Clone(),
                Times = (ushort[])this.Times.Clone(),
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };
        }
    }

    /// <summary>
    /// 配方集合（JSON序列化根对象）
    /// </summary>
    public class RecipeCollection
    {
        /// <summary>
        /// 用户自建配方列表
        /// </summary>
        public List<LoadRecipe> UserRecipes { get; set; } = new List<LoadRecipe>();
    }
}
