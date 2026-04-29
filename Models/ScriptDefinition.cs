using System;
using System.Collections.Generic;

namespace ToolBox.Models
{
    /// <summary>
    /// 脚本定义。
    /// </summary>
    public class ScriptDefinition
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 展示名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 描述说明。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 脚本类型。
        /// </summary>
        public string ScriptType { get; set; } = ScriptTypes.Batch;

        /// <summary>
        /// 原始文件名。
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 相对存储路径。
        /// </summary>
        public string RelativeScriptPath { get; set; } = string.Empty;

        /// <summary>
        /// 工作目录，留空时使用脚本所在目录。
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 是否收藏。
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// 是否默认在新终端中运行。
        /// </summary>
        public bool IsRunInTerminal { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 参数定义列表。
        /// </summary>
        public List<ScriptParameterDefinition> Parameters { get; set; } = new();
    }

    /// <summary>
    /// 脚本类型常量。
    /// </summary>
    public static class ScriptTypes
    {
        public const string Batch = "Batch";
        public const string PowerShell = "PowerShell";
        public const string Shell = "Shell";

        public static readonly string[] All = new[]
        {
            Batch,
            PowerShell,
            Shell
        };
    }
}
