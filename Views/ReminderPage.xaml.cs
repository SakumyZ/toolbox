using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Models;
using ToolBox.Services;
using Windows.UI;

namespace ToolBox.Views
{
    /// <summary>
    /// 定时器管理页面
    /// </summary>
    public sealed partial class ReminderPage : Page
    {
        private readonly ReminderService _reminderService = new();
        private readonly ScriptManagerService _scriptManagerService = new();
        private readonly ObservableCollection<ReminderViewModel> _reminders = new();
        private bool _isLoaded;

        public ReminderPage()
        {
            InitializeComponent();
            ReminderList.ItemsSource = _reminders;
            ReminderSchedulerService.Instance.RemindersChanged += ReminderScheduler_RemindersChanged;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            await RefreshDataAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            ReminderSchedulerService.Instance.RemindersChanged -= ReminderScheduler_RemindersChanged;
        }

        private void ReminderScheduler_RemindersChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadReminderRowsAsync("✓ 定时器状态已更新");
            });
        }

        private async System.Threading.Tasks.Task RefreshDataAsync(string? statusMessage = null)
        {
            await LoadReminderRowsAsync(statusMessage);
            await ReminderSchedulerService.Instance.CheckNowAsync();
        }

        private System.Threading.Tasks.Task LoadReminderRowsAsync(string? statusMessage = null)
        {
            _reminders.Clear();
            foreach (var reminder in _reminderService.GetAllReminders())
            {
                var latestLog = reminder.ActionType == ReminderActionTypes.Script
                    ? _scriptManagerService.GetLatestExecutionLogByReminder(reminder.Id)
                    : null;
                _reminders.Add(new ReminderViewModel(reminder, latestLog));
            }

            CountText.Text = $"({_reminders.Count} 个提醒)";
            StatusMessage.Text = statusMessage ?? string.Empty;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private async void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ReminderEditDialog
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await RefreshDataAsync("✓ 提醒已保存");
            }
        }

        private async void EditReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long reminderId)
            {
                return;
            }

            var reminder = _reminderService.GetReminder(reminderId);
            if (reminder == null)
            {
                return;
            }

            var dialog = new ReminderEditDialog(reminder)
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await RefreshDataAsync("✓ 提醒已更新");
            }
        }

        private async void DeleteReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long reminderId)
            {
                return;
            }

            var reminder = _reminderService.GetReminder(reminderId);
            if (reminder == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "删除提醒",
                Content = $"确定删除提醒“{reminder.Title}”吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _reminderService.DeleteReminder(reminderId);
                await RefreshDataAsync("✓ 提醒已删除");
            }
        }

        private async void ToggleReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long reminderId)
            {
                return;
            }

            var reminder = _reminderService.GetReminder(reminderId);
            if (reminder == null)
            {
                return;
            }

            _reminderService.SetReminderEnabled(reminderId, !reminder.IsEnabled);
            await RefreshDataAsync(!reminder.IsEnabled ? "✓ 提醒已启用" : "✓ 提醒已停用");
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync("✓ 已刷新");
        }

        private async void ViewReminderLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long reminderId)
            {
                return;
            }

            var logs = _scriptManagerService.GetExecutionLogsByReminder(reminderId);
            if (logs.Count == 0)
            {
                StatusMessage.Text = "当前定时器暂无脚本执行日志";
                return;
            }

            var logItems = new ObservableCollection<ReminderExecutionLogItemViewModel>(
                logs.Select(log => new ReminderExecutionLogItemViewModel(log)));

            var logList = new ListView
            {
                ItemsSource = logItems,
                SelectionMode = ListViewSelectionMode.Single,
                MinHeight = 180,
                MaxHeight = 220
            };
            logList.ItemTemplate = BuildLogItemTemplate();

            var detailBox = new TextBox
            {
                Visibility = Visibility.Collapsed,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                MinHeight = 300,
                MaxHeight = 420
            };
            ScrollViewer.SetVerticalScrollBarVisibility(detailBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(detailBox, ScrollBarVisibility.Auto);

            logList.SelectionChanged += (_, _) =>
            {
                if (logList.SelectedItem is ReminderExecutionLogItemViewModel item)
                {
                    detailBox.Visibility = Visibility.Visible;
                    detailBox.Text = BuildExecutionLogOutput(item.Log);
                }
            };

            var content = new StackPanel
            {
                MinWidth = 760,
                Spacing = 12
            };
            content.Children.Add(new TextBlock
            {
                Text = "点击上方记录查看完整命令、参数、stdout 与 stderr。",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            content.Children.Add(logList);
            content.Children.Add(detailBox);

            var dialog = new ContentDialog
            {
                Title = "脚本执行日志",
                Content = content,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private static DataTemplate BuildLogItemTemplate()
        {
            const string xaml = """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid ColumnDefinitions="115,70,70,70,*" Padding="6,4">
                        <TextBlock Text="{Binding StartedAtText}" FontFamily="Consolas" FontSize="12" />
                        <TextBlock Grid.Column="1" Text="{Binding StatusText}" FontSize="12" />
                        <TextBlock Grid.Column="2" Text="{Binding ExitCodeText}" FontSize="12" />
                        <TextBlock Grid.Column="3" Text="{Binding DurationText}" FontSize="12" />
                        <TextBlock Grid.Column="4" Text="{Binding Summary}" FontSize="12"
                                   TextTrimming="CharacterEllipsis" />
                    </Grid>
                </DataTemplate>
                """;

            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        private static string BuildExecutionLogOutput(ScriptExecutionLog log)
        {
            return
                $"[{log.StartedAt:yyyy-MM-dd HH:mm:ss}] 来源：{ResolveLogSource(log.Source)} 状态：{ResolveLogStatus(log.Status)}{Environment.NewLine}" +
                $"[{(log.FinishedAt.HasValue ? log.FinishedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "运行中")}] {log.Message}{Environment.NewLine}" +
                $"ExitCode: {(log.ExitCode.HasValue ? log.ExitCode.Value.ToString() : "-")}{Environment.NewLine}" +
                $"耗时: {FormatDuration(log.DurationMs)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- COMMAND ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(log.CommandPreview) ? "(empty)" : log.CommandPreview)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- WORKING DIRECTORY ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(log.WorkingDirectory) ? "(empty)" : log.WorkingDirectory)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- PARAMETERS ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(log.ParameterSnapshotJson) ? "[]" : log.ParameterSnapshotJson)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- STDOUT ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(log.StandardOutput) ? "(empty)" : log.StandardOutput)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- STDERR ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(log.StandardError) ? "(empty)" : log.StandardError)}";
        }

        private static string ResolveLogSource(string source)
        {
            return source == ScriptExecutionSources.Reminder ? "定时器" : "手动";
        }

        private static string ResolveLogStatus(string status)
        {
            return status switch
            {
                ScriptExecutionStatuses.Running => "运行中",
                ScriptExecutionStatuses.Success => "成功",
                ScriptExecutionStatuses.Failed => "失败",
                ScriptExecutionStatuses.Launched => "已启动",
                _ => status
            };
        }

        private static string FormatDuration(long? durationMs)
        {
            if (!durationMs.HasValue)
            {
                return "-";
            }

            return durationMs.Value < 1000
                ? $"{durationMs.Value} ms"
                : $"{durationMs.Value / 1000.0:F1} s";
        }
    }

    /// <summary>
    /// 提醒列表展示模型
    /// </summary>
    public class ReminderViewModel
    {
        private static readonly SolidColorBrush EnabledBrush = new(Color.FromArgb(255, 16, 137, 62));
        private static readonly SolidColorBrush DisabledBrush = new(Color.FromArgb(255, 140, 140, 140));

        private static readonly SolidColorBrush WaterBrush = new(Color.FromArgb(255, 66, 165, 245));
        private static readonly SolidColorBrush StandUpBrush = new(Color.FromArgb(255, 102, 187, 106));
        private static readonly SolidColorBrush MeetingBrush = new(Color.FromArgb(255, 255, 167, 38));
        private static readonly SolidColorBrush OffWorkBrush = new(Color.FromArgb(255, 171, 71, 188));
        private static readonly SolidColorBrush ScriptBrush = new(Color.FromArgb(255, 239, 83, 80));
        private static readonly SolidColorBrush CustomBrush = new(Color.FromArgb(255, 144, 164, 174));
        private static readonly SolidColorBrush WhiteTextBrush = new(Color.FromArgb(255, 255, 255, 255));

        private readonly Reminder _reminder;

        private readonly ScriptExecutionLog? _latestLog;

        public ReminderViewModel(Reminder reminder, ScriptExecutionLog? latestLog)
        {
            _reminder = reminder;
            _latestLog = latestLog;
        }

        public long Id => _reminder.Id;
        public string Category => _reminder.Category;
        public string Title => _reminder.Title;
        public string Message => string.IsNullOrWhiteSpace(_reminder.Message) ? "-" : _reminder.Message;
        public string FrequencyText => _reminder.GetFrequencyDescription();
        public string NextTriggerText => _reminder.IsEnabled
            ? _reminder.GetNextTriggerTime().ToString("MM-dd HH:mm")
            : "-";
        public string LastTriggeredText => _reminder.LastTriggeredAt?.ToString("MM-dd HH:mm") ?? "从未触发";
        public string LatestLogSummary => _latestLog == null
            ? string.Empty
            : $"{ResolveLogStatus(_latestLog.Status)} · {FormatLogDuration(_latestLog.DurationMs)} · {_latestLog.Message}";
        public string StatusText => _reminder.IsEnabled ? "启用" : "停用";
        public SolidColorBrush StatusBrush => _reminder.IsEnabled ? EnabledBrush : DisabledBrush;
        public string ToggleGlyph => _reminder.IsEnabled ? "\xE769" : "\xE768";
        public bool IsScriptAction => _reminder.ActionType == ReminderActionTypes.Script;

        public SolidColorBrush CategoryBackgroundBrush => _reminder.Category switch
        {
            ReminderPresets.Water => WaterBrush,
            ReminderPresets.StandUp => StandUpBrush,
            ReminderPresets.Meeting => MeetingBrush,
            ReminderPresets.OffWork => OffWorkBrush,
            ReminderPresets.Script => ScriptBrush,
            _ => CustomBrush
        };

        public SolidColorBrush CategoryForegroundBrush => WhiteTextBrush;

        private static string ResolveLogStatus(string status)
        {
            return status switch
            {
                ScriptExecutionStatuses.Running => "运行中",
                ScriptExecutionStatuses.Success => "成功",
                ScriptExecutionStatuses.Failed => "失败",
                ScriptExecutionStatuses.Launched => "已启动",
                _ => status
            };
        }

        private static string FormatLogDuration(long? durationMs)
        {
            if (!durationMs.HasValue)
            {
                return "-";
            }

            return durationMs.Value < 1000
                ? $"{durationMs.Value} ms"
                : $"{durationMs.Value / 1000.0:F1} s";
        }
    }

    /// <summary>
    /// 定时器脚本执行日志展示模型。
    /// </summary>
    public class ReminderExecutionLogItemViewModel
    {
        public ReminderExecutionLogItemViewModel(ScriptExecutionLog log)
        {
            Log = log;
        }

        public ScriptExecutionLog Log { get; }
        public string StartedAtText => Log.StartedAt.ToString("MM-dd HH:mm:ss");
        public string StatusText => Log.Status switch
        {
            ScriptExecutionStatuses.Running => "运行中",
            ScriptExecutionStatuses.Success => "成功",
            ScriptExecutionStatuses.Failed => "失败",
            ScriptExecutionStatuses.Launched => "已启动",
            _ => Log.Status
        };
        public string ExitCodeText => Log.ExitCode.HasValue ? Log.ExitCode.Value.ToString() : "-";
        public string DurationText => Log.DurationMs.HasValue
            ? (Log.DurationMs.Value < 1000 ? $"{Log.DurationMs.Value} ms" : $"{Log.DurationMs.Value / 1000.0:F1} s")
            : "-";
        public string Summary => string.IsNullOrWhiteSpace(Log.Message) ? "-" : Log.Message;
    }
}
