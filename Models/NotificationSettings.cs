namespace ToolBox.Models
{
    /// <summary>
    /// 通知自动关闭时长约束。
    /// </summary>
    public static class NotificationAutoCloseDurations
    {
        /// <summary>
        /// 默认自动关闭时长（毫秒）。
        /// </summary>
        public const int Default = 6000;

        /// <summary>
        /// 最小自动关闭时长（毫秒）。
        /// </summary>
        public const int Minimum = 100;

        /// <summary>
        /// 最大自动关闭时长（毫秒）。
        /// </summary>
        public const int Maximum = 600000;
    }

    /// <summary>
    /// 通知显示样式枚举。
    /// </summary>
    public enum NotificationDisplayStyle
    {
        /// <summary>
        /// 标准尺寸（完整高度）。
        /// </summary>
        Standard = 0,

        /// <summary>
        /// 紧凑尺寸（较小高度）。
        /// </summary>
        Compact = 1
    }

    /// <summary>
    /// 通知颜色主题枚举。
    /// </summary>
    public enum NotificationColorTheme
    {
        /// <summary>
        /// 跟随系统主题。
        /// </summary>
        System = 0,

        /// <summary>
        /// 浅色主题。
        /// </summary>
        Light = 1,

        /// <summary>
        /// 深色主题。
        /// </summary>
        Dark = 2
    }

    /// <summary>
    /// 通知设置配置。
    /// </summary>
    public sealed class NotificationSettings
    {
        /// <summary>
        /// 通知显示样式。
        /// </summary>
        public NotificationDisplayStyle DisplayStyle { get; set; } = NotificationDisplayStyle.Standard;

        /// <summary>
        /// 通知颜色主题。
        /// </summary>
        public NotificationColorTheme ColorTheme { get; set; } = NotificationColorTheme.System;

        /// <summary>
        /// 自动关闭时长（毫秒）。
        /// </summary>
        public int AutoCloseMilliseconds { get; set; } = NotificationAutoCloseDurations.Default;
    }
}