namespace ToolBox.Models
{
    /// <summary>
    /// 脚本参数定义。
    /// </summary>
    public class ScriptParameterDefinition
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 所属脚本 ID。
        /// </summary>
        public long ScriptId { get; set; }

        /// <summary>
        /// 参数内部名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 参数展示名称。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 控件类型。
        /// </summary>
        public string ControlType { get; set; } = ScriptParameterControlTypes.Text;

        /// <summary>
        /// 命令行参数名，例如 -input / --output。
        /// </summary>
        public string ArgumentName { get; set; } = string.Empty;

        /// <summary>
        /// 默认值。
        /// </summary>
        public string DefaultValue { get; set; } = string.Empty;

        /// <summary>
        /// 占位提示。
        /// </summary>
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>
        /// 帮助文本。
        /// </summary>
        public string HelpText { get; set; } = string.Empty;

        /// <summary>
        /// 是否必填。
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// 排序号。
        /// </summary>
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 参数控件类型常量。
    /// </summary>
    public static class ScriptParameterControlTypes
    {
        public const string Text = "Text";
        public const string Multiline = "Multiline";
        public const string Boolean = "Boolean";

        public static readonly string[] All = new[]
        {
            Text,
            Multiline,
            Boolean
        };

        public static string GetDisplayName(string controlType)
        {
            return controlType switch
            {
                Text => "文本(Text)",
                Multiline => "多行文本(Multiline)",
                Boolean => "布尔(Boolean)",
                _ => controlType
            };
        }
    }
}
