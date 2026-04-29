using System;

namespace ToolBox.Models
{
    /// <summary>
    /// 脚本执行结果。
    /// </summary>
    public class ScriptExecutionResult
    {
        /// <summary>
        /// 是否成功启动并执行。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 退出码。
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 标准输出。
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// 标准错误。
        /// </summary>
        public string StandardError { get; set; } = string.Empty;

        /// <summary>
        /// 执行摘要消息。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 开始时间。
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        public DateTime FinishedAt { get; set; }
    }
}
