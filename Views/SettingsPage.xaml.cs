using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;
using Serilog;

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

            // 加载日志级别设置
            string currentLevel = LogService.LevelSwitch.MinimumLevel.ToString();
            for (int i = 0; i < LogLevelBox.Items.Count; i++)
            {
                if (LogLevelBox.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), currentLevel, StringComparison.OrdinalIgnoreCase))
                {
                    LogLevelBox.SelectedIndex = i;
                    break;
                }
            }

            UpdateStaticPreview();
            UpdatePreviewTipText();
            LoadWebDavSettings();
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
                // 下班提醒预览：使用 offwork.svg 配合紫色背景
                imageUri = "ms-appx:///Assets/offwork.svg";
                iconBgColor = "#AB47BC";
            }
            else if (string.Equals(categoryName, ReminderPresets.Script, System.StringComparison.OrdinalIgnoreCase))
            {
                // 脚本执行预览：使用 script.svg 配合红色背景
                imageUri = "ms-appx:///Assets/script.svg";
                iconBgColor = "#EF5350";
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
                // 下班提醒静态预览：使用 offwork.svg 配合紫色背景
                imageUri = "ms-appx:///Assets/offwork.svg";
                iconBgColor = "#AB47BC";
            }
            else if (string.Equals(categoryName, ReminderPresets.Script, System.StringComparison.OrdinalIgnoreCase))
            {
                // 脚本执行静态预览：使用 script.svg 配合红色背景
                imageUri = "ms-appx:///Assets/script.svg";
                iconBgColor = "#EF5350";
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

        private void LogLevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || LogLevelBox == null || LogLevelBox.SelectedItem is not ComboBoxItem item || item.Tag == null)
            {
                return;
            }

            if (Enum.TryParse<Serilog.Events.LogEventLevel>(item.Tag.ToString(), out var level))
            {
                LogService.UpdateLogLevel(level);
            }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            LogService.OpenLogFolder();
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogService.ClearLogs();
            var dialog = new ContentDialog
            {
                Title = "清理完成",
                Content = "历史日志已清理完毕（当前日志文件因占用可能无法删除）。",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        // ========== 备份与同步 ==========

        private void LoadWebDavSettings()
        {
            try
            {
                var backupService = new BackupService();
                var webdav = backupService.GetWebDavSettings();
                WebDavUrlBox.Text = webdav.Url;
                WebDavUsernameBox.Text = webdav.Username;
                WebDavPasswordBox.Password = webdav.Password;
                WebDavAutoSyncCheckBox.IsChecked = webdav.IsAutoSyncEnabled;
                WebDavDirectoryBox.Text = webdav.Directory;
                WebDavMaxBackupCountBox.Value = webdav.MaxBackupCount;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SettingsPage: 加载 WebDAV 设置失败");
            }
        }

        private void SaveWebDavSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupService = new BackupService();
                var settings = new WebDavSettings
                {
                    Url = WebDavUrlBox.Text.Trim(),
                    Username = WebDavUsernameBox.Text.Trim(),
                    Password = WebDavPasswordBox.Password,
                    IsAutoSyncEnabled = WebDavAutoSyncCheckBox.IsChecked == true,
                    Directory = WebDavDirectoryBox.Text.Trim(),
                    MaxBackupCount = double.IsNaN(WebDavMaxBackupCountBox.Value) ? 10 : (int)WebDavMaxBackupCountBox.Value
                };
                backupService.SaveWebDavSettings(settings);
                ShowStatus("WebDAV 设置已保存。");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("保存失败", ex.Message);
            }
        }

        private async void TestWebDavConnection_Click(object sender, RoutedEventArgs e)
        {
            SetSyncStatus("正在测试连接 WebDAV 服务器...", isError: false);
            try
            {
                var url = WebDavUrlBox.Text.Trim();
                var username = WebDavUsernameBox.Text.Trim();
                var password = WebDavPasswordBox.Password;
                var directory = WebDavDirectoryBox.Text.Trim();

                var backupService = new BackupService();
                var ok = await backupService.TestWebDavConnectionAsync(url, username, password, directory);
                if (ok)
                {
                    SetSyncStatus("连接测试成功！可以正常与 WebDAV 服务器通信。", isError: false);
                }
                else
                {
                    SetSyncStatus("连接测试失败，请检查 URL、账号和密码是否正确，或者网络是否连通。", isError: true);
                }
            }
            catch (Exception ex)
            {
                SetSyncStatus($"连接测试异常: {ex.Message}", isError: true);
            }
        }

        private async void SyncWebDavNow_Click(object sender, RoutedEventArgs e)
        {
            SetSyncStatus("正在双向同步配置...", isError: false);
            try
            {
                var url = WebDavUrlBox.Text.Trim();
                var username = WebDavUsernameBox.Text.Trim();
                var password = WebDavPasswordBox.Password;
                var directory = WebDavDirectoryBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(url))
                {
                    SetSyncStatus("同步失败：服务器地址 (URL) 不能为空。", isError: true);
                    return;
                }

                var backupService = new BackupService();
                var (result, message) = await backupService.SyncWithWebDavAsync(url, username, password, directory);
                
                if (result == SyncResult.Failed)
                {
                    SetSyncStatus(message, isError: true);
                }
                else
                {
                    SetSyncStatus($"同步完成：{message}", isError: false);
                    if (result == SyncResult.Downloaded)
                    {
                        ShowRestartPromptDialog();
                    }
                }
            }
            catch (Exception ex)
            {
                SetSyncStatus($"同步失败：{ex.Message}", isError: true);
            }
        }

        private async void UploadWebDav_Click(object sender, RoutedEventArgs e)
        {
            SetSyncStatus("正在上传本地备份到云端...", isError: false);
            var tempZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox", "TempUpload.zip");
            try
            {
                var url = WebDavUrlBox.Text.Trim();
                var username = WebDavUsernameBox.Text.Trim();
                var password = WebDavPasswordBox.Password;
                var directory = WebDavDirectoryBox.Text.Trim();
                var maxCount = double.IsNaN(WebDavMaxBackupCountBox.Value) ? 10 : (int)WebDavMaxBackupCountBox.Value;

                if (string.IsNullOrWhiteSpace(url))
                {
                    SetSyncStatus("上传失败：服务器地址 (URL) 不能为空。", isError: true);
                    return;
                }

                var backupService = new BackupService();
                backupService.CreateBackupZip(tempZip);
                var remoteFileName = $"toolbox_backup_{DateTime.Now:yyyyMMddHHmmss}.zip";
                await backupService.UploadBackupToWebDavAsync(url, username, password, directory, tempZip, remoteFileName);
                await backupService.CleanOldBackupsAsync(url, username, password, directory, maxCount);
                SetSyncStatus("已成功将本地备份上传至 WebDAV 云端。", isError: false);
            }
            catch (Exception ex)
            {
                SetSyncStatus($"上传备份失败：{ex.Message}", isError: true);
            }
            finally
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
        }

        private async void DownloadWebDav_Click(object sender, RoutedEventArgs e)
        {
            var url = WebDavUrlBox.Text.Trim();
            var username = WebDavUsernameBox.Text.Trim();
            var password = WebDavPasswordBox.Password;
            var directory = WebDavDirectoryBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                SetSyncStatus("拉取失败：服务器地址 (URL) 不能为空。", isError: true);
                return;
            }

            var dialog = new BackupDataManagerDialog(url, username, password, directory)
            {
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void ExportBackup_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeChoices.Add("Zip 压缩包", new System.Collections.Generic.List<string> { ".zip" });
            picker.SuggestedFileName = $"ToolBox_Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            var window = App.MainWindowInstance;
            if (window != null)
            {
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            try
            {
                var backupService = new BackupService();
                backupService.CreateBackupZip(file.Path);
                ShowStatus("本地备份导出成功。");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("备份失败", ex.Message);
            }
        }

        private async void ImportBackup_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".zip");

            var window = App.MainWindowInstance;
            if (window != null)
            {
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            }

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            var confirmDialog = new ContentDialog
            {
                Title = "确认导入备份",
                Content = "导入该备份将完全覆盖本地现有的配置数据库和脚本文件，操作不可逆。确认导入并重启应用吗？",
                PrimaryButtonText = "确认导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                var backupService = new BackupService();
                backupService.RestoreFromZip(file.Path);
                ShowRestartPromptDialog();
            }
            catch (Exception ex)
            {
                ShowErrorDialog("导入失败", ex.Message);
            }
        }

        private void SetSyncStatus(string text, bool isError)
        {
            SyncStatusText.Text = text;
            SyncStatusText.Foreground = isError
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private void ShowStatus(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "操作成功",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private void ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private void ShowRestartPromptDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "需要重启应用",
                Content = "配置导入或恢复成功！应用需要立即重启以加载新数据。",
                PrimaryButtonText = "立即重启",
                CloseButtonText = "稍后手动重启",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
            };

            _ = dialog.ShowAsync();
        }
    }
}