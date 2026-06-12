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

        /// <summary>
        /// 目标 Agent 目录列表。
        /// </summary>
        public System.Collections.Generic.List<string> AgentDirectories { get; set; } = new();
    }
}