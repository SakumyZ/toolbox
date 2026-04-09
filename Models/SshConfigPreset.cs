using System;

namespace ToolBox.Models
{
    /// <summary>
    /// SSH config 预设。
    /// </summary>
    public class SshConfigPreset
    {
        /// <summary>
        /// 唯一标识。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 预设名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 预设说明。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 整份 ssh config 文本内容。
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 是否当前激活。
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 最后使用时间。
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

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