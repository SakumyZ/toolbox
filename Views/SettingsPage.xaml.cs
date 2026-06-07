using System;
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

            UpdateStaticPreview();
            UpdatePreviewTipText();
        }

        private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStaticPreview();
        }

        private void DisplayStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStaticPreview();
        }

        private void ColorThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStaticPreview();
        }

        private void AutoCloseMillisecondsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // 用户改变时实时更新，但不自动保存（需要点保存按钮）
            UpdatePreviewTipText();
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var settings = GetCurrentSettings();
            var categoryIndex = CategoryBox.SelectedIndex;
            if (categoryIndex < 0) categoryIndex = 0;
            string categoryName = ReminderPresets.Categories[categoryIndex];
            var (title, message) = ReminderPresets.ResolveTemplate(categoryName);
            
            string imageUri = "ms-appx:///Assets/default.svg";
            string iconBgColor = "#90A4AE";

            if (string.Equals(categoryName, ReminderPresets.Water, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/water.svg";
                iconBgColor = "#42A5F5";
            }
            else if (string.Equals(categoryName, ReminderPresets.StandUp, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/standup.svg";
                iconBgColor = "#66BB6A";
            }
            else if (string.Equals(categoryName, ReminderPresets.Meeting, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/meeting.svg";
                iconBgColor = "#FFA726";
            }
            else if (string.Equals(categoryName, ReminderPresets.OffWork, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/offwork.svg";
                iconBgColor = "#AB47BC";
            }

            var visual = new NotificationVisual
            {
                Title = title,
                Message = message,
                FooterText = System.DateTime.Now.ToString("HH:mm"),
                ImageUri = imageUri,
                IconBackgroundColor = iconBgColor,
                DisplayStyle = settings.DisplayStyle,
                ColorTheme = settings.ColorTheme,
                AutoCloseMilliseconds = settings.AutoCloseMilliseconds
            };

            var windowManager = new NotificationWindowManager();
            windowManager.Show(visual);
        }

        private void UpdatePreviewTipText()
        {
            if (PreviewTipText == null || AutoCloseMillisecondsBox == null)
            {
                return;
            }

            int ms = GetSelectedAutoCloseMilliseconds();
            double seconds = Math.Round(ms / 1000.0, 1);
            string secondsStr = seconds.ToString("0.#");
            PreviewTipText.Text = $"提示：预览通知会按当前设置显示一条样本通知，{secondsStr} 秒后自动关闭。";
        }

        private void UpdateStaticPreview()
        {
            if (!_isLoaded || DisplayStyleBox == null || ColorThemeBox == null || CategoryBox == null)
            {
                return;
            }

            var styleIndex = DisplayStyleBox.SelectedIndex; // 0: Standard, 1: Compact
            var themeIndex = ColorThemeBox.SelectedIndex; // 0: System, 1: Light, 2: Dark
            var categoryIndex = CategoryBox.SelectedIndex;
            if (categoryIndex < 0) categoryIndex = 0;

            // 1. 获取通知分类对应的模板
            string categoryName = ReminderPresets.Categories[categoryIndex];
            var (title, message) = ReminderPresets.ResolveTemplate(categoryName);
            
            // 2. 根据分类解析图标与背景色
            string imageUri = "ms-appx:///Assets/default.svg";
            string iconBgColor = "#90A4AE"; // 默认灰色

            if (string.Equals(categoryName, ReminderPresets.Water, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/water.svg";
                iconBgColor = "#42A5F5";
            }
            else if (string.Equals(categoryName, ReminderPresets.StandUp, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/standup.svg";
                iconBgColor = "#66BB6A";
            }
            else if (string.Equals(categoryName, ReminderPresets.Meeting, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/meeting.svg";
                iconBgColor = "#FFA726";
            }
            else if (string.Equals(categoryName, ReminderPresets.OffWork, System.StringComparison.OrdinalIgnoreCase))
            {
                imageUri = "ms-appx:///Assets/offwork.svg";
                iconBgColor = "#AB47BC";
            }

            // 更新静态文本
            PreviewTitleText.Text = title;
            PreviewMessageText.Text = message;
            PreviewFooterText.Text = System.DateTime.Now.ToString("HH:mm");

            // 3. 应用尺寸布局
            if (styleIndex == 1) // Compact
            {
                StaticPreviewBorder.Height = 96;
                PreviewMessageText.MaxLines = 1;
                PreviewMessageText.Margin = new Thickness(0, 2, 0, 4);
            }
            else // Standard
            {
                StaticPreviewBorder.Height = 120;
                PreviewMessageText.MaxLines = 2;
                PreviewMessageText.Margin = new Thickness(0, 4, 0, 6);
            }

            // 4. 加载图片
            try
            {
                var resolvedUri = NotificationWindow.ResolveResourceUri(imageUri);
                PreviewImageControl.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(resolvedUri);
            }
            catch
            {
                PreviewImageControl.Source = null;
            }

            // 5. 应用主题颜色（包括背景色与前景字色）
            var theme = themeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            StaticPreviewBorder.RequestedTheme = theme;

            if (theme == ElementTheme.Light)
            {
                var whiteBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                StaticPreviewBorder.Background = whiteBrush;
                PreviewImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (theme == ElementTheme.Dark)
            {
                StaticPreviewBorder.ClearValue(Border.BackgroundProperty);
                PreviewImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0x00, 0x00, 0x00));
            }
            else
            {
                StaticPreviewBorder.ClearValue(Border.BackgroundProperty);
                PreviewImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0x00, 0x00, 0x00));
            }

            // 设置左侧图标背景色
            var bgBrush = NotificationWindow.ParseHexColor(iconBgColor);
            if (bgBrush != null)
            {
                PreviewImageColumnBorder.Background = bgBrush;
            }

            // 重新解析前景字色，避免缓存覆盖
            PreviewTitleText.ClearValue(TextBlock.ForegroundProperty);
            PreviewMessageText.ClearValue(TextBlock.ForegroundProperty);
            PreviewFooterText.ClearValue(TextBlock.ForegroundProperty);
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