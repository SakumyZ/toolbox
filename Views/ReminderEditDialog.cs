using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 提醒新增/编辑对话框
    /// </summary>
    public sealed class ReminderEditDialog : ContentDialog
    {
        private readonly ReminderService _reminderService = new();
        private readonly ScriptManagerService _scriptManagerService = new();
        private readonly Reminder? _editingReminder;
        private readonly System.Collections.Generic.List<ScriptDefinition> _scripts;

        private ComboBox _actionTypeCombo = null!;
        private ComboBox _scriptCombo = null!;

        private ComboBox _categoryCombo = null!;
        private ComboBox _recurrenceCombo = null!;
        private TextBox _titleBox = null!;
        private TextBox _messageBox = null!;
        private NumberBox _intervalBox = null!;
        private CalendarDatePicker _datePicker = null!;
        private TimePicker _timePicker = null!;
        private StackPanel _timePanel = null!;
        private NumberBox _dayOfMonthBox = null!;
        private TextBlock _timeHintText = null!;
        private ToggleSwitch _enabledSwitch = null!;

        public ReminderEditDialog(Reminder? reminder = null)
        {
            _editingReminder = reminder;
            _scripts = _scriptManagerService.GetAllScripts();
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            Title = _editingReminder == null ? "新增定时器" : "编辑定时器";
            PrimaryButtonText = "保存";
            CloseButtonText = "取消";
            DefaultButton = ContentDialogButton.Primary;
            PrimaryButtonClick += OnPrimaryButtonClick;

            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 420
            };

            _actionTypeCombo = new ComboBox
            {
                Header = "任务类型"
            };
            foreach (var actionType in ReminderActionTypes.All)
            {
                _actionTypeCombo.Items.Add(actionType);
            }
            _actionTypeCombo.SelectionChanged += ActionTypeCombo_SelectionChanged;
            panel.Children.Add(_actionTypeCombo);

            _categoryCombo = new ComboBox
            {
                Header = "提醒类型"
            };
            foreach (var category in ReminderPresets.Categories)
            {
                // 如果是“执行脚本”预设，不要作为通知的类型供普通提醒选择（脚本定时有专门的任务类型来管理）
                if (category == ReminderPresets.Script)
                {
                    continue;
                }
                _categoryCombo.Items.Add(category);
            }
            _categoryCombo.SelectionChanged += CategoryCombo_SelectionChanged;
            panel.Children.Add(_categoryCombo);

            _scriptCombo = new ComboBox
            {
                Header = "选择脚本",
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id",
                ItemsSource = _scripts,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_scriptCombo);

            _recurrenceCombo = new ComboBox
            {
                Header = "提醒频率"
            };
            foreach (var recurrenceType in ReminderRecurrenceTypes.All)
            {
                _recurrenceCombo.Items.Add(recurrenceType);
            }
            _recurrenceCombo.SelectionChanged += RecurrenceCombo_SelectionChanged;
            panel.Children.Add(_recurrenceCombo);

            _titleBox = new TextBox
            {
                Header = "通知标题",
                PlaceholderText = "例如：喝水时间到了"
            };
            panel.Children.Add(_titleBox);

            _messageBox = new TextBox
            {
                Header = "通知内容",
                PlaceholderText = "例如：起来接杯水，顺便活动一下",
                AcceptsReturn = true,
                Height = 80,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_messageBox);

            _intervalBox = new NumberBox
            {
                Header = "间隔分钟",
                Minimum = 1,
                Maximum = 1440,
                SmallChange = 5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_intervalBox);

            _datePicker = new CalendarDatePicker
            {
                Header = "提醒日期 (选填)",
                PlaceholderText = "留空则指今天/明天",
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_datePicker);

            _timePicker = new TimePicker
            {
                Header = "提醒时间",
                ClockIdentifier = "24HourClock",
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var setNowButton = new Button
            {
                Content = "当前时间",
                VerticalAlignment = VerticalAlignment.Bottom
            };
            setNowButton.Click += SetNowButton_Click;

            _timePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };
            _timePanel.Children.Add(_timePicker);
            _timePanel.Children.Add(setNowButton);
            panel.Children.Add(_timePanel);

            _dayOfMonthBox = new NumberBox
            {
                Header = "每月日期",
                Minimum = 1,
                Maximum = 31,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_dayOfMonthBox);

            _timeHintText = new TextBlock
            {
                Text = "单次提醒到点后会自动停用。",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 12
            };
            panel.Children.Add(_timeHintText);

            _enabledSwitch = new ToggleSwitch
            {
                Header = "启用提醒",
                OnContent = "启用",
                OffContent = "停用"
            };
            panel.Children.Add(_enabledSwitch);

            Content = panel;

            LoadInitialValues();
        }

        private void LoadInitialValues()
        {
            if (_editingReminder != null)
            {
                _actionTypeCombo.SelectedItem = _editingReminder.ActionType;
                _categoryCombo.SelectedItem = _editingReminder.Category;
                if (_editingReminder.ActionType == ReminderActionTypes.Script && _editingReminder.ScriptId.HasValue)
                {
                    _scriptCombo.SelectedValue = _editingReminder.ScriptId.Value;
                }
                _recurrenceCombo.SelectedItem = _editingReminder.RecurrenceType;
                _titleBox.Text = _editingReminder.Title;
                _messageBox.Text = _editingReminder.Message;
                _enabledSwitch.IsOn = _editingReminder.IsEnabled;
                _intervalBox.Value = _editingReminder.IntervalMinutes <= 0 ? 60 : _editingReminder.IntervalMinutes;
                _dayOfMonthBox.Value = _editingReminder.DayOfMonth <= 0 ? 1 : _editingReminder.DayOfMonth;
                _timePicker.Time = TimeSpan.TryParse(_editingReminder.TimeText, out var time)
                    ? time
                    : new TimeSpan(9, 0, 0);
                if (!string.IsNullOrWhiteSpace(_editingReminder.DateText) && DateTime.TryParse(_editingReminder.DateText, out var parsedDate))
                {
                    _datePicker.Date = parsedDate;
                }
                UpdateFrequencyFields();
                return;
            }

            _actionTypeCombo.SelectedItem = ReminderActionTypes.Notification;
            _categoryCombo.SelectedItem = ReminderPresets.Water;
            _recurrenceCombo.SelectedItem = ReminderRecurrenceTypes.Single;
            _datePicker.Date = null;
            _timePicker.Time = new TimeSpan(10, 0, 0);
            _intervalBox.Value = 60;
            _dayOfMonthBox.Value = 1;
            _enabledSwitch.IsOn = true;
            ApplyPreset(ReminderPresets.Water, force: true);
            UpdateFrequencyFields();
            UpdateActionTypeFields();
        }

        private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionTypeFields();
        }

        private void UpdateActionTypeFields()
        {
            var actionType = _actionTypeCombo.SelectedItem as string ?? ReminderActionTypes.Notification;
            var isScript = actionType == ReminderActionTypes.Script;

            _categoryCombo.Visibility = isScript ? Visibility.Collapsed : Visibility.Visible;
            _messageBox.Visibility = isScript ? Visibility.Collapsed : Visibility.Visible;
            _scriptCombo.Visibility = isScript ? Visibility.Visible : Visibility.Collapsed;

            _titleBox.Header = isScript ? "任务名称" : "通知标题";
            if (isScript)
            {
                _titleBox.PlaceholderText = "例如：备份数据库";
            }
            else
            {
                _titleBox.PlaceholderText = "例如：喝水时间到了";
            }
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_categoryCombo.SelectedItem is string category)
            {
                ApplyPreset(category, force: _editingReminder == null);
            }
        }

        private void ApplyPreset(string category, bool force)
        {
            var (title, message) = ReminderPresets.ResolveTemplate(category);
            if (force || string.IsNullOrWhiteSpace(_titleBox.Text))
            {
                _titleBox.Text = title;
            }

            if (force || string.IsNullOrWhiteSpace(_messageBox.Text))
            {
                _messageBox.Text = message;
            }
        }

        private void RecurrenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFrequencyFields();
        }

        private void UpdateFrequencyFields()
        {
            var recurrenceType = _recurrenceCombo.SelectedItem as string ?? ReminderRecurrenceTypes.Single;
            _intervalBox.Visibility = recurrenceType == ReminderRecurrenceTypes.Interval
                ? Visibility.Visible
                : Visibility.Collapsed;
            _datePicker.Visibility = recurrenceType == ReminderRecurrenceTypes.Single
                ? Visibility.Visible
                : Visibility.Collapsed;
            _timePanel.Visibility = recurrenceType == ReminderRecurrenceTypes.Interval
                ? Visibility.Collapsed
                : Visibility.Visible;
            _dayOfMonthBox.Visibility = recurrenceType == ReminderRecurrenceTypes.Monthly
                ? Visibility.Visible
                : Visibility.Collapsed;

            _timeHintText.Text = recurrenceType switch
            {
                var type when type == ReminderRecurrenceTypes.Interval => "按触发时间开始，每隔 N 分钟再次提醒。",
                var type when type == ReminderRecurrenceTypes.Daily => "每天在指定时间提醒。",
                var type when type == ReminderRecurrenceTypes.Monthly => "每月指定日期和时间提醒。",
                _ => "单次提醒到点后会自动停用。"
            };
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var actionType = _actionTypeCombo.SelectedItem as string ?? ReminderActionTypes.Notification;
            var category = _categoryCombo.SelectedItem as string ?? ReminderPresets.Custom;

            if (actionType == ReminderActionTypes.Script && _scriptCombo.SelectedValue == null)
            {
                _scriptCombo.Header = "选择脚本 (必填)";
                args.Cancel = true;
                return;
            }

            if (_recurrenceCombo.SelectedItem is not string recurrenceType)
            {
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(_titleBox.Text))
            {
                _titleBox.Header = "通知标题 (必填)";
                args.Cancel = true;
                return;
            }

            var reminder = _editingReminder ?? new Reminder();
            reminder.ActionType = actionType;
            reminder.Category = actionType == ReminderActionTypes.Script ? "执行脚本" : category;
            reminder.ScriptId = actionType == ReminderActionTypes.Script ? (long?)_scriptCombo.SelectedValue : null;
            reminder.Title = _titleBox.Text.Trim();
            reminder.Message = _messageBox.Text.Trim();
            reminder.RecurrenceType = recurrenceType;
            reminder.TimeText = _timePicker.Time.ToString(@"hh\:mm");
            reminder.DateText = recurrenceType == ReminderRecurrenceTypes.Single && _datePicker.Date.HasValue
                ? _datePicker.Date.Value.ToString("yyyy-MM-dd")
                : string.Empty;
            reminder.IntervalMinutes = (int)Math.Round(_intervalBox.Value);
            reminder.DayOfMonth = (int)Math.Round(_dayOfMonthBox.Value);
            reminder.IsEnabled = _enabledSwitch.IsOn;
            if (reminder.CreatedAt == default)
            {
                reminder.CreatedAt = DateTime.Now;
            }

            if (recurrenceType == ReminderRecurrenceTypes.Interval && reminder.IntervalMinutes <= 0)
            {
                _intervalBox.Header = "间隔分钟 (必须大于 0)";
                args.Cancel = true;
                return;
            }

            if (recurrenceType == ReminderRecurrenceTypes.Monthly && (reminder.DayOfMonth < 1 || reminder.DayOfMonth > 31))
            {
                _dayOfMonthBox.Header = "每月日期 (1-31)";
                args.Cancel = true;
                return;
            }

            if (recurrenceType == ReminderRecurrenceTypes.Single)
            {
                reminder.LastTriggeredAt = null;
            }

            _reminderService.SaveReminder(reminder);
        }

        private void SetNowButton_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now.TimeOfDay;
            _timePicker.Time = new TimeSpan(now.Hours, now.Minutes, 0);
        }
    }
}