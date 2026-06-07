using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 设置页面，用于配置通知样式、主题和自动关闭时长。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly NotificationSettingsService _settingsService = new();
        private bool _isLoaded;

        public SettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 页面首次加载时初始化数据。
        /// </summary>
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 避免重复加载。
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            LoadSettings();
        }

        /// <summary>
        /// 从服务加载当前设置并填充 UI 控件。
        /// </summary>
        private void LoadSettings()
        {
            var settings = _settingsService.GetSettings();

            // 设置显示样式
            DisplayStyleBox.SelectedIndex = (int)settings.DisplayStyle;

            // 设置颜色主题
            ColorThemeBox.SelectedIndex = (int)settings.ColorTheme;

            // 设置自动关闭毫秒数
            AutoCloseMillisecondsBox.Value = settings.AutoCloseMilliseconds;
        }

        /// <summary>
        /// 显示样式改变时的处理。
        /// </summary>
        private void DisplayStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 用户改变时实时更新，但不自动保存（需要点保存按钮）
        }

        /// <summary>
        /// 颜色主题改变时的处理。
        /// </summary>
        private void ColorThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 用户改变时实时更新，但不自动保存（需要点保存按钮）
        }

        /// <summary>
        /// 自动关闭时长改变时的处理。
        /// </summary>
        private void AutoCloseMillisecondsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // 用户改变时实时更新，但不自动保存（需要点保存按钮）
        }

        /// <summary>
        /// 预览通知按钮点击处理。
        /// </summary>
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var settings = GetCurrentSettings();

            var visual = new NotificationVisual
            {
                Title = "通知预览",
                Message = $"样式: {settings.DisplayStyle}, 主题: {settings.ColorTheme}",
                FooterText = System.DateTime.Now.ToString("HH:mm"),
                ImageUri = "ms-appx:///Assets/default.svg",
                IconBackgroundColor = "#90A4AE",
                DisplayStyle = settings.DisplayStyle,
                ColorTheme = settings.ColorTheme,
                AutoCloseMilliseconds = 3000  // 预览通知 3 秒后关闭
            };

            var windowManager = new NotificationWindowManager();
            windowManager.Show(visual);
        }

        /// <summary>
        /// 保存设置按钮点击处理。
        /// </summary>
        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = GetCurrentSettings();
            _settingsService.SaveSettings(settings);

            var dialog = new ContentDialog
            {
                Title = "保存成功",
                Content = "通知设置已保存。",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        /// <summary>
        /// 从 UI 控件获取当前设置。
        /// </summary>
        private NotificationSettings GetCurrentSettings()
        {
            return new NotificationSettings
            {
                DisplayStyle = (NotificationDisplayStyle)DisplayStyleBox.SelectedIndex,
                ColorTheme = (NotificationColorTheme)ColorThemeBox.SelectedIndex,
                AutoCloseMilliseconds = GetSelectedAutoCloseMilliseconds()
            };
        }

        /// <summary>
        /// 获取并验证自动关闭毫秒数。
        /// </summary>
        private int GetSelectedAutoCloseMilliseconds()
        {
            if (double.IsNaN(AutoCloseMillisecondsBox.Value))
            {
                return NotificationAutoCloseDurations.Default;
            }

            var ms = (int)AutoCloseMillisecondsBox.Value;
            return _settingsService.NormalizeAutoCloseMilliseconds(ms);
        }
    }
}