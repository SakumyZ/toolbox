using System;

namespace ToolBox.Models
{
    /// <summary>
    /// Skill 元数据。
    /// </summary>
    public class SkillMetadata
    {
        /// <summary>
        /// Skill 文件夹名。
        /// </summary>
        public string SkillId { get; set; } = string.Empty;

        /// <summary>
        /// Skill 别名。
        /// </summary>
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// Skill 分类。
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}