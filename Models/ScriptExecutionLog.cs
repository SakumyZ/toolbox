using System;

namespace ToolBox.Models
{
    /// <summary>
    /// 脚本执行日志。
    /// </summary>
    public class ScriptExecutionLog
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 关联脚本 ID。
        /// </summary>
        public long ScriptId { get; set; }

        /// <summary>
        /// 关联定时器 ID，手动执行时为空。
        /// </summary>
        public long? ReminderId { get; set; }

        /// <summary>
        /// 执行来源。
        /// </summary>
        public string Source { get; set; } = ScriptExecutionSources.Manual;

        /// <summary>
        /// 执行状态。
        /// </summary>
        public string Status { get; set; } = ScriptExecutionStatuses.Running;

        /// <summary>
        /// 退出码。
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// 开始时间。
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>
        /// 执行耗时，毫秒。
        /// </summary>
        public long? DurationMs { get; set; }

        /// <summary>
        /// 命令预览。
        /// </summary>
        public string CommandPreview { get; set; } = string.Empty;

        /// <summary>
        /// 工作目录。
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 参数快照 JSON。
        /// </summary>
        public string ParameterSnapshotJson { get; set; } = string.Empty;

        /// <summary>
        /// 标准输出。
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// 标准错误。
        /// </summary>
        public string StandardError { get; set; } = string.Empty;

        /// <summary>
        /// 执行摘要。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 归档时间。
        /// </summary>
        public DateTime? ArchivedAt { get; set; }
    }

    /// <summary>
    /// 脚本执行来源常量。
    /// </summary>
    public static class ScriptExecutionSources
    {
        public const string Manual = "Manual";
        public const string Reminder = "Reminder";
    }

    /// <summary>
    /// 脚本执行状态常量。
    /// </summary>
    public static class ScriptExecutionStatuses
    {
        public const string Running = "Running";
        public const string Success = "Success";
        public const string Failed = "Failed";
        public const string Launched = "Launched";
    }
}
