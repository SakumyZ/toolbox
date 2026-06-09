using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Models;
using ToolBox.Services;
using Windows.UI;

namespace ToolBox.Views
{
    public sealed partial class DashboardPage : Page
    {
        private const int MaxVisibleReminderRows = 5;

        private readonly ReminderService _reminderService = new();
        private readonly SshConfigService _sshConfigService = new();
        private bool _isSubscribed;

        public DashboardPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCardsAsync();
            if (_isSubscribed)
            {
                return;
            }

            DashboardCardRegistry.ProvidersChanged += OnProvidersChanged;
            ReminderSchedulerService.Instance.RemindersChanged += OnRemindersChanged;
            _isSubscribed = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isSubscribed)
            {
                return;
            }

            DashboardCardRegistry.ProvidersChanged -= OnProvidersChanged;
            ReminderSchedulerService.Instance.RemindersChanged -= OnRemindersChanged;
            _isSubscribed = false;
        }

        private void OnProvidersChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadCardsAsync();
            });
        }

        private void OnRemindersChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadCardsAsync();
            });
        }

        private async System.Threading.Tasks.Task LoadCardsAsync()
        {
            CardsPanel.Children.Clear();

            var providers = DashboardCardRegistry.GetAll();
            var reminders = _reminderService.GetAllReminders();
            var enabledReminderCount = reminders.Count(reminder => reminder.IsEnabled);
            int totalFavorites = 0;

            CardsPanel.Children.Add(BuildReminderCard(reminders));
            CardsPanel.Children.Add(BuildSshConfigCard());

            foreach (var provider in providers)
            {
                var favorites = await provider.GetFavoritesAsync();
                totalFavorites += favorites.Count;

                // 卡片容器
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16)
                };

                var cardContent = new StackPanel { Spacing = 10 };

                // 卡片标题行
                var titleRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                    }
                };

                var icon = new FontIcon
                {
                    Glyph = provider.IconGlyph,
                    FontSize = 16,
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                };
                Grid.SetColumn(icon, 0);
                titleRow.Children.Add(icon);

                var titleText = new TextBlock
                {
                    Text = provider.CardTitle,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(titleText, 1);
                titleRow.Children.Add(titleText);

                var countBadge = new TextBlock
                {
                    Text = $"{favorites.Count} 项",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(countBadge, 2);
                titleRow.Children.Add(countBadge);

                cardContent.Children.Add(titleRow);

                if (favorites.Count > 0)
                {
                    cardContent.Children.Add(BuildFavoriteCardsGrid(favorites));
                }
                else
                {
                    var emptyText = new TextBlock
                    {
                        Text = "暂无收藏",
                        Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                        FontSize = 12
                    };
                    cardContent.Children.Add(emptyText);
                }

                // 查看更多按钮
                var viewAllButton = new HyperlinkButton
                {
                    Content = $"查看全部 {provider.CardTitle} →",
                    Tag = provider.NavigationTag,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                viewAllButton.Click += ViewAll_Click;
                cardContent.Children.Add(viewAllButton);

                card.Child = cardContent;
                CardsPanel.Children.Add(card);
            }

            var sshPresets = _sshConfigService.GetAllPresets();
            SummaryText.Text = $"运行中定时器 {enabledReminderCount} 个，SSH 预设 {sshPresets.Count} 个，收藏模块 {providers.Count} 个，收藏 {totalFavorites} 项";
        }

        /**
         * 构建 SSH Config 预设卡片。
         * @returns {UIElement} 返回构建好的卡片控件。
         */
        private UIElement BuildSshConfigCard()
        {
            var presets = _sshConfigService.GetAllPresets();
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var content = new StackPanel { Spacing = 10 };

            // 构建卡片头部
            var titleRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                }
            };

            var icon = new FontIcon
            {
                Glyph = "\xE128",
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            };
            Grid.SetColumn(icon, 0);
            titleRow.Children.Add(icon);

            var titleText = new TextBlock
            {
                Text = "SSH Config 预设",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(titleText, 1);
            titleRow.Children.Add(titleText);

            var countBadge = new TextBlock
            {
                Text = $"共 {presets.Count} 个预设",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countBadge, 2);
            titleRow.Children.Add(countBadge);

            content.Children.Add(titleRow);

            if (presets.Count == 0)
            {
                // 分支：没有 SSH 预设，显示提示
                content.Children.Add(new TextBlock
                {
                    Text = "暂无 SSH Config 预设，请到管理界面导入或新建",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    FontSize = 12
                });
            }
            else
            {
                // 分支：存在 SSH 预设，使用横向 Radio Group 渲染
                var radioGroupPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };

                foreach (var preset in presets)
                {
                    var rb = new RadioButton
                    {
                        GroupName = "SshConfigPresetsGroup",
                        IsChecked = preset.IsActive,
                        Tag = preset.Id,
                        Margin = new Thickness(0, 0, 8, 0)
                    };

                    var rbContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    rbContent.Children.Add(new TextBlock
                    {
                        Text = preset.Name,
                        FontWeight = preset.IsActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
                    });

                    if (!string.IsNullOrEmpty(preset.Description))
                    {
                        // 分支：预设带有说明，展示说明
                        rbContent.Children.Add(new TextBlock
                        {
                            Text = $"- {preset.Description}",
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    rb.Content = rbContent;
                    rb.Click += SshPreset_Click;
                    radioGroupPanel.Children.Add(rb);
                }

                content.Children.Add(radioGroupPanel);
            }

            var manageButton = new HyperlinkButton
            {
                Content = "管理 SSH 配置 →",
                Tag = "SshConfig",
                Margin = new Thickness(0, 4, 0, 0)
            };
            manageButton.Click += ViewAll_Click;
            content.Children.Add(manageButton);

            card.Child = content;
            return card;
        }

        /**
         * 处理 SSH 预设被选中的事件。
         * @param {object} sender 事件发送者。
         * @param {RoutedEventArgs} e 事件参数。
         */
        private async void SshPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is long presetId)
            {
                // 分支：确认点击的是有效 RadioButton
                if (rb.IsChecked == true)
                {
                    // 分支：RadioButton 处于 Checked 状态，进行激活操作
                    var result = _sshConfigService.ActivatePreset(presetId);
                    
                    if (result.success)
                    {
                        // 分支：切换成功，重新加载 Dashboard 以更新高亮状态
                        await LoadCardsAsync();
                    }
                    else
                    {
                        // 分支：切换失败，向用户弹出错误对话框，并重新加载以还原正确的 Checked 状态
                        var dialog = new ContentDialog
                        {
                            Title = "切换失败",
                            Content = result.message,
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        await dialog.ShowAsync();
                        await LoadCardsAsync();
                    }
                }
            }
        }

        private UIElement BuildReminderCard(System.Collections.Generic.IReadOnlyList<Reminder> reminders)
        {
            var enabledReminders = reminders
                .Where(reminder => reminder.IsEnabled)
                .OrderBy(reminder => reminder.GetNextTriggerTime())
                .ThenBy(reminder => reminder.Title)
                .Take(MaxVisibleReminderRows)
                .ToList();
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(BuildReminderHeader(reminders.Count, reminders.Count(reminder => reminder.IsEnabled)));

            if (enabledReminders.Count > 0)
            {
                content.Children.Add(BuildReminderCardsGrid(enabledReminders));
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = "暂无运行中的定时器",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    FontSize = 12
                });
            }

            var manageButton = new HyperlinkButton
            {
                Content = "管理全部定时器 →",
                Tag = "Reminder",
                Margin = new Thickness(0, 4, 0, 0)
            };
            manageButton.Click += ViewAll_Click;
            content.Children.Add(manageButton);

            card.Child = content;
            return card;
        }

        private UIElement BuildFavoriteCardsGrid(System.Collections.Generic.IReadOnlyList<DashboardFavoriteItem> favorites)
        {
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 10
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < favorites.Count; i++)
            {
                if (i % 3 == 0)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                var favoriteCard = BuildFavoriteItemCard(favorites[i]);
                Grid.SetRow(favoriteCard, i / 3);
                Grid.SetColumn(favoriteCard, i % 3);
                grid.Children.Add(favoriteCard);
            }

            return grid;
        }

        private FrameworkElement BuildFavoriteItemCard(DashboardFavoriteItem favorite)
        {
            var favoriteButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Tag = new NavTarget(favorite.NavigationTag, favorite.NavigationParameter)
            };
            favoriteButton.Click += FavoriteItem_Click;

            var card = new Grid
            {
                MinHeight = 76,
                Padding = new Thickness(12, 10, 12, 10)
            };

            var itemContent = new StackPanel { Spacing = 4 };
            itemContent.Children.Add(BuildFavoriteTitleLine(favorite));

            if (!string.IsNullOrEmpty(favorite.TypeText))
            {
                itemContent.Children.Add(new TextBlock
                {
                    Text = favorite.TypeText,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            card.Children.Add(itemContent);
            favoriteButton.Content = card;
            return favoriteButton;
        }

        private static UIElement BuildFavoriteTitleLine(DashboardFavoriteItem favorite)
        {
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            titleRow.Children.Add(new TextBlock
            {
                Text = favorite.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!string.IsNullOrEmpty(favorite.StatusText))
            {
                var statusBorder = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = favorite.StatusBrush ??
                        (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                statusBorder.Child = new TextBlock
                {
                    Text = favorite.StatusText,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
                };
                titleRow.Children.Add(statusBorder);
            }

            return titleRow;
        }

        private static UIElement BuildReminderHeader(int totalCount, int enabledCount)
        {
            var titleRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                }
            };

            var icon = new FontIcon
            {
                Glyph = "\xE916",
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            };
            Grid.SetColumn(icon, 0);
            titleRow.Children.Add(icon);

            var titleText = new TextBlock
            {
                Text = "定时器",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(titleText, 1);
            titleRow.Children.Add(titleText);

            var countBadge = new TextBlock
            {
                Text = $"{enabledCount}/{totalCount} 运行中",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countBadge, 2);
            titleRow.Children.Add(countBadge);

            return titleRow;
        }

        private UIElement BuildReminderCardsGrid(System.Collections.Generic.IReadOnlyList<Reminder> reminders)
        {
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 10
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < reminders.Count; i++)
            {
                if (i % 3 == 0)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                var reminderCard = BuildReminderItemCard(reminders[i]);
                Grid.SetRow(reminderCard, i / 3);
                Grid.SetColumn(reminderCard, i % 3);
                grid.Children.Add(reminderCard);
            }

            return grid;
        }

        private FrameworkElement BuildReminderItemCard(Reminder reminder)
        {
            var card = new Grid
            {
                MinHeight = 76,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                },
                Padding = new Thickness(12, 10, 10, 10)
            };

            var details = new StackPanel { Spacing = 4 };
            details.Children.Add(BuildReminderTitleLine(reminder));
            details.Children.Add(new TextBlock
            {
                Text = BuildReminderScheduleText(reminder),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(details, 0);
            card.Children.Add(details);

            var toggleButton = new Button
            {
                Tag = reminder.Id,
                Padding = new Thickness(7, 5, 7, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(toggleButton, "关闭");
            toggleButton.Content = new FontIcon
            {
                Glyph = "\xE769",
                FontSize = 14
            };
            toggleButton.Click += ReminderToggle_Click;
            Grid.SetColumn(toggleButton, 1);
            card.Children.Add(toggleButton);

            return new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(6),
                Child = card
            };
        }

        private static UIElement BuildReminderTitleLine(Reminder reminder)
        {
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            titleRow.Children.Add(new TextBlock
            {
                Text = reminder.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var badge = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = ResolveReminderCategoryBrush(reminder)
            };
            badge.Child = new TextBlock
            {
                Text = reminder.ActionType == ReminderActionTypes.Script ? "脚本" : reminder.Category,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            };
            titleRow.Children.Add(badge);

            return titleRow;
        }

        private static string BuildReminderScheduleText(Reminder reminder)
        {
            return $"{reminder.GetFrequencyDescription()} · 下次 {reminder.GetNextTriggerTime():MM-dd HH:mm}";
        }

        private static SolidColorBrush ResolveReminderCategoryBrush(Reminder reminder)
        {
            if (reminder.ActionType == ReminderActionTypes.Script)
            {
                return new SolidColorBrush(Color.FromArgb(255, 239, 83, 80));
            }

            return reminder.Category switch
            {
                ReminderPresets.Water => new SolidColorBrush(Color.FromArgb(255, 66, 165, 245)),
                ReminderPresets.StandUp => new SolidColorBrush(Color.FromArgb(255, 102, 187, 106)),
                ReminderPresets.Meeting => new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),
                ReminderPresets.OffWork => new SolidColorBrush(Color.FromArgb(255, 171, 71, 188)),
                _ => new SolidColorBrush(Color.FromArgb(255, 144, 164, 174))
            };
        }

        private void FavoriteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is NavTarget target)
            {
                if (string.IsNullOrWhiteSpace(target.Tag))
                {
                    return;
                }

                NavigateToPage(target.Tag, target.Parameter);
            }
        }

        private void ViewAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton button && button.Tag is string navTag)
            {
                NavigateToPage(navTag);
            }
        }

        private async void ReminderToggle_Click(object sender, RoutedEventArgs e)
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

            _reminderService.SetReminderEnabled(reminder.Id, !reminder.IsEnabled);
            await LoadCardsAsync();
            await ReminderSchedulerService.Instance.CheckNowAsync();
        }

        /// <summary>
        /// 导航到指定模块页，可携带参数。
        /// </summary>
        private void NavigateToPage(string pageTag, object? parameter = null)
        {
            if (App.MainWindowInstance is MainWindow mainWindow)
            {
                mainWindow.NavigateTo(pageTag, parameter);
            }
        }

        /// <summary>
        /// 导航目标封装（Tag + 可选参数）。
        /// </summary>
        private sealed record NavTarget(string? Tag, object? Parameter);
    }
}
