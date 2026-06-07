namespace ToolBox.Models
{
    /// <summary>
    /// 自定义通知窗的展示数据。
    /// </summary>
    public sealed class NotificationVisual
    {
        /// <summary>
        /// 通知标题。
        /// </summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// 通知正文。
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// 通知底部辅助文本。
        /// </summary>
        public string FooterText { get; init; } = string.Empty;

        /// <summary>
        /// 左侧图片资源。
        /// </summary>
        public string ImageUri { get; init; } = "ms-appx:///Assets/Square150x150Logo.scale-200.png";

        /// <summary>
        /// 左侧图标栏背景色（十六进制字符串，如 #42A5F5），若为空则使用系统主题色（Accent）。
        /// </summary>
        public string? IconBackgroundColor { get; init; }

        /// <summary>
        /// 通知显示样式。
        /// </summary>
        public NotificationDisplayStyle DisplayStyle { get; init; } = NotificationDisplayStyle.Standard;

        /// <summary>
        /// 通知颜色主题。
        /// </summary>
        public NotificationColorTheme ColorTheme { get; init; } = NotificationColorTheme.System;

        /// <summary>
        /// 自动关闭时长（毫秒）。
        /// </summary>
        public int AutoCloseMilliseconds { get; init; } = 6000;
    }
}