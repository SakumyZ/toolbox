namespace ToolBox.Models
{
    /// <summary>
    /// 代码片段文件夹/分组
    /// </summary>
    public class SnippetFolder
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 文件夹名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 父文件夹 ID（0 表示根级别）
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int SortOrder { get; set; }
    }
}
