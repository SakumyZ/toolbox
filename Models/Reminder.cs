using System;
using System.Collections.Generic;

namespace ToolBox.Models
{
    /// <summary>
    /// 提醒频率类型
    /// </summary>
    public static class ReminderRecurrenceTypes
    {
        public const string Single = "单次";
        public const string Interval = "间隔";
        public const string Daily = "每天";
        public const string Monthly = "每月";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Single,
            Interval,
            Daily,
            Monthly
        };
    }

    /// <summary>
    /// 提醒动作类型
    /// </summary>
    public static class ReminderActionTypes
    {
        public const string Notification = "通知提醒";
        public const string Script = "执行脚本";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Notification,
            Script
        };
    }

    /// <summary>
    /// 定时提醒实体
    /// </summary>
    public class Reminder
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 提醒分类
        /// </summary>
        public string Category { get; set; } = ReminderPresets.Custom;

        /// <summary>
        /// 通知标题
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 动作类型
        /// </summary>
        public string ActionType { get; set; } = ReminderActionTypes.Notification;

        /// <summary>
        /// 关联的脚本 ID（如果 ActionType 是 Script）
        /// </summary>
        public long? ScriptId { get; set; }

        /// <summary>
        /// 脚本执行参数的 JSON 序列化字符串
        /// </summary>
        public string ScriptParameters { get; set; } = string.Empty;

        /// <summary>
        /// 通知内容
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 每日触发时间，格式 HH:mm
        /// </summary>
        public string TimeText { get; set; } = "09:00";

        /// <summary>
        /// 触发日期（供单次使用），格式 yyyy-MM-dd，为空代表每天轮询
        /// </summary>
        public string DateText { get; set; } = string.Empty;

        /// <summary>
        /// 提醒频率
        /// </summary>
        public string RecurrenceType { get; set; } = ReminderRecurrenceTypes.Single;

        /// <summary>
        /// 间隔分钟数，供“间隔”频率使用
        /// </summary>
        public int IntervalMinutes { get; set; }

        /// <summary>
        /// 每月第几天，供“每月”频率使用
        /// </summary>
        public int DayOfMonth { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 最近一次触发时间
        /// </summary>
        public DateTime? LastTriggeredAt { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 计算下一次触发时间
        /// </summary>
        public DateTime GetNextTriggerTime(DateTime? now = null)
        {
            var current = now ?? DateTime.Now;
            return RecurrenceType switch
            {
                ReminderRecurrenceTypes.Interval => GetNextIntervalTriggerTime(current),
                ReminderRecurrenceTypes.Daily => GetNextDailyTriggerTime(current),
                ReminderRecurrenceTypes.Monthly => GetNextMonthlyTriggerTime(current),
                _ => GetNextSingleTriggerTime(current)
            };
        }

        private DateTime GetNextSingleTriggerTime(DateTime current)
        {
            if (!TimeSpan.TryParse(TimeText, out var timeOfDay))
            {
                return current;
            }

            DateTime targetDate = current.Date;
            bool hasExplicitDate = false;
            if (!string.IsNullOrWhiteSpace(DateText) && DateTime.TryParse(DateText, out var parsedDate))
            {
                hasExplicitDate = true;
                targetDate = parsedDate.Date;
            }

            var candidate = targetDate.Add(timeOfDay);
            if (LastTriggeredAt.HasValue && candidate <= LastTriggeredAt.Value)
            {
                return LastTriggeredAt.Value;
            }

            if (!hasExplicitDate && candidate <= current)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        private DateTime GetNextDailyTriggerTime(DateTime current)
        {
            if (!TimeSpan.TryParse(TimeText, out var timeOfDay))
            {
                return current;
            }

            var candidate = current.Date.Add(timeOfDay);
            if (candidate <= current)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        private DateTime GetNextMonthlyTriggerTime(DateTime current)
        {
            if (!TimeSpan.TryParse(TimeText, out var timeOfDay))
            {
                return current;
            }

            var targetDay = Math.Clamp(DayOfMonth <= 0 ? current.Day : DayOfMonth, 1, 31);
            var daysInCurrentMonth = DateTime.DaysInMonth(current.Year, current.Month);
            var currentMonthDay = Math.Min(targetDay, daysInCurrentMonth);
            var candidate = new DateTime(current.Year, current.Month, currentMonthDay,
                timeOfDay.Hours, timeOfDay.Minutes, 0);

            if (candidate <= current)
            {
                var nextMonth = current.AddMonths(1);
                var daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                var nextMonthDay = Math.Min(targetDay, daysInNextMonth);
                candidate = new DateTime(nextMonth.Year, nextMonth.Month, nextMonthDay,
                    timeOfDay.Hours, timeOfDay.Minutes, 0);
            }

            return candidate;
        }

        private DateTime GetNextIntervalTriggerTime(DateTime current)
        {
            var minutes = IntervalMinutes <= 0 ? 60 : IntervalMinutes;
            if (!LastTriggeredAt.HasValue)
            {
                return current.AddMinutes(minutes);
            }

            var nextTrigger = LastTriggeredAt.Value.AddMinutes(minutes);
            if (nextTrigger <= current)
            {
                return current.AddMinutes(minutes);
            }

            return nextTrigger;
        }

        /// <summary>
        /// 频率描述
        /// </summary>
        public string GetFrequencyDescription()
        {
            return RecurrenceType switch
            {
                ReminderRecurrenceTypes.Interval => $"每隔 {IntervalMinutes} 分钟",
                ReminderRecurrenceTypes.Daily => $"每天 {TimeText}",
                ReminderRecurrenceTypes.Monthly => $"每月 {DayOfMonth} 日 {TimeText}",
                _ => string.IsNullOrWhiteSpace(DateText) ? $"单次 {TimeText}" : $"单次 {DateText} {TimeText}"
            };
        }
    }

    /// <summary>
    /// 提醒预设
    /// </summary>
    public static class ReminderPresets
    {
        public const string Water = "喝水提醒";
        public const string StandUp = "起身活动";
        public const string Meeting = "会议提醒";
        public const string OffWork = "下班提醒";
        public const string Custom = "自定义";

        public static readonly IReadOnlyList<string> Categories = new[]
        {
            Water,
            StandUp,
            Meeting,
            OffWork,
            Custom
        };

        public static (string title, string message) ResolveTemplate(string category)
        {
            return category switch
            {
                Water => ("喝水时间到了", "喝几口水，顺便让眼睛休息一下。"),
                StandUp => ("起来活动一下", "站起来走两步，活动肩颈和腰背。"),
                Meeting => ("会议提醒", "检查会议时间和参会链接，提前做好准备。"),
                OffWork => ("准备下班", "收尾今天的工作，整理待办和明日计划。"),
                _ => ("提醒事项", "到时间了，处理一下这件事。")
            };
        }
    }
}