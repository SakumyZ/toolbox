using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 端口占用查看器页面
    /// </summary>
    public sealed partial class PortViewerPage : Page
    {
        private readonly ObservableCollection<PortEntryViewModel> _displayEntries = new();
        private List<PortEntry> _allEntries = new();
        private bool _isLoaded;

        public PortViewerPage()
        {
            InitializeComponent();
            PortList.ItemsSource = _displayEntries;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                _isLoaded = true;
                await RefreshData();
            }
        }

        // ========== 数据加载 ==========

        private async Task RefreshData()
        {
            LoadingRing.IsActive = true;
            PortList.Visibility = Visibility.Collapsed;

            _allEntries = await Task.Run(() => PortService.GetAllPorts());

            ApplyFilter();

            LoadingRing.IsActive = false;
            PortList.Visibility = Visibility.Visible;
        }

        private void ApplyFilter()
        {
            _displayEntries.Clear();

            var filtered = _allEntries.AsEnumerable();

            // 协议筛选
            if (ProtocolFilter.SelectedIndex == 1)
                filtered = filtered.Where(e => e.Protocol == "TCP");
            else if (ProtocolFilter.SelectedIndex == 2)
                filtered = filtered.Where(e => e.Protocol == "UDP");

            // 状态筛选
            if (StateFilter.SelectedIndex > 0)
            {
                var stateItem = StateFilter.SelectedItem as ComboBoxItem;
                var stateText = stateItem?.Content?.ToString() ?? "";
                filtered = filtered.Where(e => e.State == stateText);
            }

            // 仅监听
            if (ListenOnlyToggle.IsChecked == true)
            {
                filtered = filtered.Where(e => e.State == "LISTEN" || e.Protocol == "UDP");
            }

            // 搜索
            var search = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(e =>
                    e.LocalPort.ToString().Contains(search) ||
                    e.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    e.LocalAddress.Contains(search) ||
                    e.Pid.ToString().Contains(search));
            }

            // 排序：监听优先，然后按端口号
            var sorted = filtered
                .OrderByDescending(e => e.State == "LISTEN")
                .ThenBy(e => e.LocalPort)
                .ThenBy(e => e.Protocol);

            foreach (var entry in sorted)
            {
                _displayEntries.Add(new PortEntryViewModel(entry));
            }

            CountText.Text = $"({_displayEntries.Count} 条连接)";
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
            if (_isLoaded)
            {
                ApplyFilter();
            }
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int pid)
            {
                if (pid <= 4) return; // 不允许杀 System

                var procName = _allEntries.FirstOrDefault(x => x.Pid == pid)?.ProcessName ?? $"PID {pid}";

                var dialog = new ContentDialog
                {
                    Title = "结束进程",
                    Content = $"确定结束进程 \"{procName}\" (PID: {pid}) 吗？\n该进程占用的所有端口都会释放。",
                    PrimaryButtonText = "结束",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    bool success = PortService.KillProcess(pid);
                    if (success)
                    {
                        CountText.Text = $"✓ 已结束: {procName}";
                        // 等待进程释放端口后刷新
                        await Task.Delay(500);
                        await RefreshData();
                    }
                    else
                    {
                        CountText.Text = $"✗ 无法结束 {procName}，可能需要管理员权限";
                    }
                }
            }
        }
    }

    /// <summary>
    /// 端口条目 ViewModel
    /// </summary>
    public class PortEntryViewModel
    {
        private static readonly SolidColorBrush TcpBrush = new(Color.FromArgb(255, 56, 132, 206));   // 蓝色
        private static readonly SolidColorBrush UdpBrush = new(Color.FromArgb(255, 136, 100, 196));   // 紫色

        private static readonly SolidColorBrush ListenBrush = new(Color.FromArgb(255, 16, 137, 62));      // 绿色
        private static readonly SolidColorBrush EstablishedBrush = new(Color.FromArgb(255, 56, 132, 206)); // 蓝色
        private static readonly SolidColorBrush TimeWaitBrush = new(Color.FromArgb(255, 140, 140, 140));   // 灰色
        private static readonly SolidColorBrush CloseWaitBrush = new(Color.FromArgb(255, 200, 130, 40));   // 橙色
        private static readonly SolidColorBrush OtherStateBrush = new(Color.FromArgb(255, 100, 100, 100)); // 暗灰

        private static readonly SolidColorBrush CommonPortBrush = new(Color.FromArgb(255, 220, 160, 50));  // 金色
        private static readonly SolidColorBrush NormalPortBrush = new(Color.FromArgb(255, 200, 200, 200)); // 默认

        private readonly PortEntry _entry;

        public PortEntryViewModel(PortEntry entry)
        {
            _entry = entry;
        }

        public string Protocol => _entry.Protocol;
        public string LocalAddress => _entry.LocalAddress;
        public string LocalPortText => _entry.LocalPort.ToString();
        public string RemoteAddress => _entry.RemoteAddress;
        public string RemotePortText => _entry.RemotePort.ToString();
        public string State => _entry.State;
        public int Pid => _entry.Pid;
        public string PidText => _entry.Pid.ToString();
        public string ProcessName => _entry.ProcessName;
        public string ProcessPath => string.IsNullOrEmpty(_entry.ProcessPath) ? _entry.ProcessName : _entry.ProcessPath;

        public SolidColorBrush ProtocolBrush => _entry.Protocol == "TCP" ? TcpBrush : UdpBrush;

        public SolidColorBrush PortBrush =>
            PortService.CommonDevPorts.Contains(_entry.LocalPort) ? CommonPortBrush : NormalPortBrush;

        public Visibility IsCommonPortVis =>
            PortService.CommonDevPorts.Contains(_entry.LocalPort) ? Visibility.Visible : Visibility.Collapsed;

        public SolidColorBrush StateBrush => _entry.State switch
        {
            "LISTEN" => ListenBrush,
            "ESTABLISHED" => EstablishedBrush,
            "TIME_WAIT" => TimeWaitBrush,
            "CLOSE_WAIT" => CloseWaitBrush,
            _ => OtherStateBrush
        };
    }
}
