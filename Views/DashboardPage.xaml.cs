using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Services;

namespace ToolBox.Views
{
    public sealed partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCardsAsync();
            DashboardCardRegistry.ProvidersChanged += OnProvidersChanged;
        }

        private void OnProvidersChanged(object? sender, EventArgs e)
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
            if (providers.Count == 0)
            {
                SummaryText.Text = "暂无收藏模块注册";
                return;
            }

            int totalFavorites = 0;

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

                // 收藏项列表
                if (favorites.Count > 0)
                {
                    foreach (var fav in favorites)
                    {
                        var favButton = new Button
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(12, 8, 12, 8),
                            Margin = new Thickness(0, 0, 0, 4),
                            Tag = new NavTarget(fav.NavigationTag, fav.NavigationParameter)
                        };

                        // 收藏项内容：标题行 + 分类行
                        var itemContent = new StackPanel { Spacing = 4 };

                        // 第一行：标题 + 状态 tag
                        var titleRow2 = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8
                        };
                        titleRow2.Children.Add(new TextBlock
                        {
                            Text = fav.Title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });

                        // 状态 tag（与 PortViewer 对齐的 badge 样式）
                        if (!string.IsNullOrEmpty(fav.StatusText))
                        {
                            var statusBorder = new Border
                            {
                                CornerRadius = new CornerRadius(3),
                                Padding = new Thickness(6, 2, 6, 2),
                                VerticalAlignment = VerticalAlignment.Center,
                                Background = fav.StatusBrush ??
                                    (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                            };
                            statusBorder.Child = new TextBlock
                            {
                                Text = fav.StatusText,
                                FontSize = 10,
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                            };
                            titleRow2.Children.Add(statusBorder);
                        }

                        itemContent.Children.Add(titleRow2);

                        // 第二行：分类文本
                        if (!string.IsNullOrEmpty(fav.TypeText))
                        {
                            itemContent.Children.Add(new TextBlock
                            {
                                Text = fav.TypeText,
                                FontSize = 12,
                                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                                TextTrimming = TextTrimming.CharacterEllipsis
                            });
                        }

                        favButton.Content = itemContent;
                        favButton.Click += FavoriteItem_Click;
                        cardContent.Children.Add(favButton);
                    }
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

            SummaryText.Text = $"共 {providers.Count} 个模块，{totalFavorites} 项收藏";
        }

        private void FavoriteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is NavTarget target)
            {
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
