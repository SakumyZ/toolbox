using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// Windows 服务管理器页面
    /// </summary>
    public sealed partial class ServiceManagerPage : Page
    {
        private readonly ServiceManagerService _svc = new();
        private readonly ObservableCollection<ServiceViewModel> _displayServices = new();
        private List<ServiceInfo> _allServices = new();
        private bool _isLoaded;

        public ServiceManagerPage()
        {
            InitializeComponent();
            ServiceList.ItemsSource = _displayServices;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                _isLoaded = true;
                await RefreshData();
                LoadGroupFlyout();
            }
        }

        // ========== 数据加载 ==========

        private async Task RefreshData()
        {
            LoadingRing.IsActive = true;
            ServiceList.Visibility = Visibility.Collapsed;
            StatusMessage.Text = "加载中...";

            _allServices = await Task.Run(() => _svc.GetAllServices());

            ApplyFilter();

            LoadingRing.IsActive = false;
            ServiceList.Visibility = Visibility.Visible;
            StatusMessage.Text = "";
        }

        private void ApplyFilter()
        {
            _displayServices.Clear();

            var filtered = _allServices.AsEnumerable();

            // 状态筛选
            if (StatusFilter.SelectedIndex > 0)
            {
                var item = StatusFilter.SelectedItem as ComboBoxItem;
                var tag = item?.Tag?.ToString() ?? "";
                filtered = filtered.Where(s => s.Status == tag);
            }

            // 启动类型筛选
            if (StartTypeFilter.SelectedIndex > 0)
            {
                var item = StartTypeFilter.SelectedItem as ComboBoxItem;
                var typeText = item?.Content?.ToString() ?? "";
                filtered = filtered.Where(s => s.StartType == typeText);
            }

            // 仅收藏
            if (FavoriteOnlyToggle.IsChecked == true)
            {
                filtered = filtered.Where(s => s.IsFavorite);
            }

            // 搜索
            var search = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(s =>
                    s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            // 排序：收藏优先 → 运行中优先 → 按名称
            var sorted = filtered
                .OrderByDescending(s => s.IsFavorite)
                .ThenByDescending(s => s.Status == "Running")
                .ThenBy(s => s.DisplayName);

            foreach (var s in sorted)
            {
                _displayServices.Add(new ServiceViewModel(s));
            }

            CountText.Text = $"({_displayServices.Count} / {_allServices.Count} 个服务)";
        }

        // ========== 事件处理 ==========

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshData();
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ApplyFilter();
            }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded) ApplyFilter();
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        // ========== 服务操作 ==========

        private async void StartService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                await ExecuteServiceAction(serviceName, "启动", _svc.StartServiceAsync);
            }
        }

        private async void StopService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                await ExecuteServiceAction(serviceName, "停止", _svc.StopServiceAsync);
            }
        }

        private async void RestartService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                await ExecuteServiceAction(serviceName, "重启", _svc.RestartServiceAsync);
            }
        }

        private async Task ExecuteServiceAction(string serviceName, string actionName,
            Func<string, Task<(bool success, string message)>> action)
        {
            StatusMessage.Text = $"{actionName}中: {serviceName}...";

            var (success, message) = await action(serviceName);

            StatusMessage.Text = success ? $"✓ {message}" : $"✗ {message}";

            // 刷新该服务的状态
            var newStatus = _svc.GetServiceStatus(serviceName);
            var svc = _allServices.FirstOrDefault(s => s.ServiceName == serviceName);
            if (svc != null)
            {
                svc.Status = newStatus;
            }
            ApplyFilter();
        }

        // ========== 收藏 ==========

        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                _svc.ToggleFavorite(serviceName);

                // 更新内存中的状态
                var svc = _allServices.FirstOrDefault(s => s.ServiceName == serviceName);
                if (svc != null)
                {
                    svc.IsFavorite = !svc.IsFavorite;
                }
                ApplyFilter();
            }
        }

        // ========== 分组 ==========

        private void LoadGroupFlyout()
        {
            GroupFlyout.Items.Clear();

            var groups = _svc.GetAllGroups();

            if (groups.Count > 0)
            {
                foreach (var group in groups)
                {
                    var subItem = new MenuFlyoutSubItem
                    {
                        Text = $"{group.Name} ({group.ServiceNames.Count}个)"
                    };

                    var startItem = new MenuFlyoutItem
                    {
                        Text = "▶ 全部启动",
                        Tag = group.Id
                    };
                    startItem.Click += StartGroup_Click;
                    subItem.Items.Add(startItem);

                    var stopItem = new MenuFlyoutItem
                    {
                        Text = "■ 全部停止",
                        Tag = group.Id
                    };
                    stopItem.Click += StopGroup_Click;
                    subItem.Items.Add(stopItem);

                    subItem.Items.Add(new MenuFlyoutSeparator());

                    var deleteItem = new MenuFlyoutItem
                    {
                        Text = "删除分组",
                        Tag = group.Id
                    };
                    deleteItem.Click += DeleteGroup_Click;
                    subItem.Items.Add(deleteItem);

                    GroupFlyout.Items.Add(subItem);
                }

                GroupFlyout.Items.Add(new MenuFlyoutSeparator());
            }

            var addItem = new MenuFlyoutItem
            {
                Text = "＋ 新建分组（从收藏创建）"
            };
            addItem.Click += CreateGroup_Click;
            GroupFlyout.Items.Add(addItem);
        }

        private async void StartGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is long groupId)
            {
                StatusMessage.Text = "正在批量启动...";
                var results = await _svc.StartGroupAsync(groupId);

                int ok = results.Count(r => r.success);
                int fail = results.Count - ok;
                StatusMessage.Text = $"✓ 批量启动完成: {ok} 成功, {fail} 失败";

                await RefreshData();
            }
        }

        private async void StopGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is long groupId)
            {
                StatusMessage.Text = "正在批量停止...";
                var results = await _svc.StopGroupAsync(groupId);

                int ok = results.Count(r => r.success);
                int fail = results.Count - ok;
                StatusMessage.Text = $"✓ 批量停止完成: {ok} 成功, {fail} 失败";

                await RefreshData();
            }
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is long groupId)
            {
                _svc.DeleteGroup(groupId);
                LoadGroupFlyout();
                StatusMessage.Text = "✓ 分组已删除";
            }
        }

        private async void CreateGroup_Click(object sender, RoutedEventArgs e)
        {
            var favoriteServices = _allServices.Where(s => s.IsFavorite).ToList();
            if (favoriteServices.Count == 0)
            {
                StatusMessage.Text = "请先收藏一些服务，再创建分组";
                return;
            }

            var input = new TextBox { PlaceholderText = "分组名称（如：开发环境）" };
            var serviceListText = string.Join("\n", favoriteServices.Select(s => $"  • {s.DisplayName}"));
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(input);
            content.Children.Add(new TextBlock
            {
                Text = $"将包含以下 {favoriteServices.Count} 个收藏服务：\n{serviceListText}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = "新建服务分组",
                Content = content,
                PrimaryButtonText = "创建",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
                !string.IsNullOrWhiteSpace(input.Text))
            {
                _svc.CreateGroup(input.Text.Trim(), favoriteServices.Select(s => s.ServiceName).ToList());
                LoadGroupFlyout();
                StatusMessage.Text = $"✓ 分组「{input.Text.Trim()}」已创建";
            }
        }
    }

    // ========== ViewModel ==========

    /// <summary>
    /// 服务列表 ViewModel
    /// </summary>
    public class ServiceViewModel
    {
        private static readonly SolidColorBrush RunningBrush = new(Color.FromArgb(255, 16, 137, 62));
        private static readonly SolidColorBrush StoppedBrush = new(Color.FromArgb(255, 140, 140, 140));
        private static readonly SolidColorBrush PendingBrush = new(Color.FromArgb(255, 200, 130, 40));
        private static readonly SolidColorBrush FavGoldBrush = new(Color.FromArgb(255, 255, 200, 50));
        private static readonly SolidColorBrush FavGrayBrush = new(Color.FromArgb(255, 100, 100, 100));

        private readonly ServiceInfo _info;

        public ServiceViewModel(ServiceInfo info) => _info = info;

        public string ServiceName => _info.ServiceName;
        public string DisplayName => _info.DisplayName;
        public string StartType => _info.StartType;

        public string StatusText => _info.Status switch
        {
            "Running" => "运行中",
            "Stopped" => "已停止",
            "StartPending" => "启动中",
            "StopPending" => "停止中",
            "Paused" => "已暂停",
            "ContinuePending" => "恢复中",
            "PausePending" => "暂停中",
            _ => _info.Status
        };

        public SolidColorBrush StatusBrush => _info.Status switch
        {
            "Running" => RunningBrush,
            "Stopped" => StoppedBrush,
            _ => PendingBrush
        };

        public string FavoriteGlyph => _info.IsFavorite ? "\xE735" : "\xE734";
        public SolidColorBrush FavoriteBrush => _info.IsFavorite ? FavGoldBrush : FavGrayBrush;

        public bool CanStart => _info.Status != "Running" && _info.StartType != "禁用";
        public bool CanStop => _info.Status == "Running";
        public bool CanRestart => _info.Status == "Running";
    }
}
