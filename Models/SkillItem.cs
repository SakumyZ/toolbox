namespace ToolBox.Models
{
    /// <summary>
    /// Skill 列表项。
    /// </summary>
    public class SkillItem
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
        /// 展示名称。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Skill 描述。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 当前是否激活。
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Skill 所在目录。
        /// </summary>
        public string CurrentPath { get; set; } = string.Empty;

        /// <summary>
        /// Skill.md 文件路径。
        /// </summary>
        public string SkillFilePath { get; set; } = string.Empty;
    }
}