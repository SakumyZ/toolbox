using System;

namespace ToolBox.Models
{
    /// <summary>
    /// 提醒触发记录
    /// </summary>
    public class ReminderTriggerLog
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 关联提醒 ID
        /// </summary>
        public long ReminderId { get; set; }

        /// <summary>
        /// 触发时间
        /// </summary>
        public DateTime TriggeredAt { get; set; }

        /// <summary>
        /// 触发结果
        /// </summary>
        public string Status { get; set; } = string.Empty;
    }
}