using System;

namespace ToolBox.Models
{
    /// <summary>
    /// 代码片段实体
    /// </summary>
    public class Snippet
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 标题
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 代码内容
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 编程语言
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// 描述/备注
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 所属文件夹 ID（0 表示根目录）
        /// </summary>
        public long FolderId { get; set; }

        /// <summary>
        /// 是否收藏
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// 使用次数（复制次数）
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 关联的标签（非数据库字段，运行时填充）
        /// </summary>
        public string Tags { get; set; } = string.Empty;
    }
}
