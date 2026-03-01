namespace ToolBox.Models
{
    /// <summary>
    /// 标签实体
    /// </summary>
    public class Tag
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 标签名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 标签颜色（十六进制，如 #FF5722）
        /// </summary>
        public string Color { get; set; } = "#0078D4";
    }
}
