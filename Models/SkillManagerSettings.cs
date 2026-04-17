namespace ToolBox.Models
{
    /// <summary>
    /// Skill 管理目录配置。
    /// </summary>
    public class SkillManagerSettings
    {
        /// <summary>
        /// 已启用 skill 根目录。
        /// </summary>
        public string ActiveSkillsPath { get; set; } = string.Empty;

        /// <summary>
        /// 已停用 skill 根目录。
        /// </summary>
        public string InactiveSkillsPath { get; set; } = string.Empty;
    }
}