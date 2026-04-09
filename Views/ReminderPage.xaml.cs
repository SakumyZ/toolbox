using System;
using System.Collections.ObjectModel;
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
        private readonly ObservableCollection<ReminderViewModel> _reminders = new();
        private bool _isLoaded;

        public ReminderPage()
        {
            InitializeComponent();
            ReminderList.ItemsSource = _reminders;
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

        private async System.Threading.Tasks.Task RefreshDataAsync(string? statusMessage = null)
        {
            _reminders.Clear();
            foreach (var reminder in _reminderService.GetAllReminders())
            {
                _reminders.Add(new ReminderViewModel(reminder));
            }

            CountText.Text = $"({_reminders.Count} 个提醒)";
            StatusMessage.Text = statusMessage ?? string.Empty;
            await ReminderSchedulerService.Instance.CheckNowAsync();
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
    }

    /// <summary>
    /// 提醒列表展示模型
    /// </summary>
    public class ReminderViewModel
    {
        private static readonly SolidColorBrush EnabledBrush = new(Color.FromArgb(255, 16, 137, 62));
        private static readonly SolidColorBrush DisabledBrush = new(Color.FromArgb(255, 140, 140, 140));

        private readonly Reminder _reminder;

        public ReminderViewModel(Reminder reminder)
        {
            _reminder = reminder;
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
        public string StatusText => _reminder.IsEnabled ? "启用" : "停用";
        public SolidColorBrush StatusBrush => _reminder.IsEnabled ? EnabledBrush : DisabledBrush;
        public string ToggleGlyph => _reminder.IsEnabled ? "\xE769" : "\xE768";
    }
}