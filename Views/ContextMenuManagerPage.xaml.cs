using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 右键菜单管理器页面类。
    /// </summary>
    public sealed partial class ContextMenuManagerPage : Page
    {
        private readonly ContextMenuService _service = new();
        private readonly ObservableCollection<ContextMenuItem> _fileItems = new();
        private readonly ObservableCollection<ContextMenuItem> _directoryItems = new();
        private readonly ObservableCollection<ContextMenuItem> _backgroundItems = new();

        /// <summary>
        /// 指示全局是否已启用 Windows 10 经典右键风格，供静态 x:Bind 绑定评估级别使用。
        /// </summary>
        public static bool IsClassicMenuStatic { get; set; } = false;

        /// <summary>
        /// 初始化页面与数据绑定源。
        /// </summary>
        public ContextMenuManagerPage()
        {
            InitializeComponent();
            FileList.ItemsSource = _fileItems;
            DirectoryList.ItemsSource = _directoryItems;
            BackgroundList.ItemsSource = _backgroundItems;
        }

        /// <summary>
        /// 供 x:Bind 数据绑定调用的布尔值到 UI 可见性转换函数。
        /// </summary>
        /// <param name="value">布尔输入值。</param>
        /// <returns>若为 true 则返回 Visible，否则返回 Collapsed。</returns>
        public static Visibility BoolToVisibility(bool value)
        {
            // 分支注释：布尔值转换为 WinUI 的 Visibility 枚举值
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 供 x:Bind 数据绑定调用的布尔值到 UI 可见性反向转换函数。
        /// </summary>
        /// <param name="value">布尔输入值。</param>
        /// <returns>若为 true 则返回 Collapsed，否则返回 Visible。</returns>
        public static Visibility BoolToInverseVisibility(bool value)
        {
            // 分支注释：布尔值反向转换为 WinUI 的 Visibility 枚举值
            return value ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 供 x:Bind 数据绑定评估右键菜单项分级的静态函数。
        /// </summary>
        /// <param name="isShellExt">是否是动态扩展项。</param>
        /// <returns>级别的文本显示。</returns>
        public static string GetLevelText(bool isShellExt)
        {
            // 分支注释：如果开启了 Windows 10 经典右键样式，所有项目无差别地挂载在第一级
            if (IsClassicMenuStatic)
            {
                return "第 1 级 (经典)";
            }
            
            // 分支注释：在 Win11 默认下，动态扩展 (shellex) 是第一级，静态菜单 (shell) 是第二级 (更多选项)
            return isShellExt ? "第 1 级" : "第 2 级";
        }

        /// <summary>
        /// 供 x:Bind 数据绑定获取级别气泡颜色的静态函数。
        /// </summary>
        /// <param name="isShellExt">是否是动态扩展项。</param>
        /// <returns>SolidColorBrush 颜色画刷。</returns>
        public static Microsoft.UI.Xaml.Media.SolidColorBrush GetLevelBrush(bool isShellExt)
        {
            // 分支注释：如果是第一级（包括开启经典样式或动态扩展），返回主题蓝色气泡
            if (IsClassicMenuStatic || isShellExt)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));
            }
            
            // 分支注释：如果是第二级（折叠菜单），返回中性深灰色气泡
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 112, 112, 112));
        }

        /// <summary>
        /// 页面加载时的事件处理程序，用于刷新状态和列表。
        /// </summary>
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 分支注释：只有在 Windows 11 (Build >= 22000) 下才显示一键切换 Win10 经典样式的设置卡片
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                StyleSettingsCard.Visibility = Visibility.Visible;
                IsClassicMenuStatic = _service.IsClassicMenuEnabled();
                
                // 暂时移除事件订阅防止初始化触发逻辑，然后再绑定
                ClassicStyleToggle.Toggled -= ClassicStyleToggle_Toggled;
                ClassicStyleToggle.IsOn = IsClassicMenuStatic;
                ClassicStyleToggle.Toggled += ClassicStyleToggle_Toggled;
            }
            else
            {
                StyleSettingsCard.Visibility = Visibility.Collapsed;
                IsClassicMenuStatic = true; // Win10/Win7 等系统本来就是一级的经典右键
            }

            RefreshPermissionBanner();
            RefreshAllData();
        }

        /// <summary>
        /// 检查当前管理员权限，并更新顶部的状态 Banner。
        /// </summary>
        private void RefreshPermissionBanner()
        {
            bool isAdmin = _service.IsAdmin();
            // 分支注释：如果是管理员，显示绿色的成功信息条，并隐藏提权重启按钮
            if (isAdmin)
            {
                AdminInfoBar.Severity = InfoBarSeverity.Success;
                AdminInfoBar.Message = "已启用管理员模式。可以管理所有系统全局及用户级右键菜单。";
                ElevateButton.Visibility = Visibility.Collapsed;
            }
            // 分支注释：如果非管理员，显示警告信息条，并展示以管理员重新运行的按钮
            else
            {
                AdminInfoBar.Severity = InfoBarSeverity.Warning;
                AdminInfoBar.Message = "当前处于非管理员模式。仅能修改用户级菜单，全局系统菜单将处于只读状态。";
                ElevateButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 刷新三个作用域的列表数据并更新总数统计。
        /// </summary>
        private void RefreshAllData()
        {
            RefreshList(ContextMenuScope.File, _fileItems);
            RefreshList(ContextMenuScope.Directory, _directoryItems);
            RefreshList(ContextMenuScope.Background, _backgroundItems);
            UpdateCountText();
        }

        /// <summary>
        /// 刷新单个特定作用域的列表集合。
        /// </summary>
        private void RefreshList(ContextMenuScope scope, ObservableCollection<ContextMenuItem> collection)
        {
            collection.Clear();
            var list = _service.GetMenuItems(scope);
            foreach (var item in list)
            {
                collection.Add(item);
            }

            // 分支注释：在后台线程异步提取文件和动态扩展关联的图标，避免阻塞 UI 渲染
            _ = Task.Run(() =>
            {
                foreach (var item in list)
                {
                    // 分支注释：如果注册表中存在有效的图标路径或关联的 DLL/EXE
                    if (!string.IsNullOrWhiteSpace(item.IconPath))
                    {
                        // 1. 后台线程安全提取 PNG 二进制字节流 (避免 GDI+ 操作卡顿 UI)
                        byte[]? pngBytes = IconExtractor.ExtractIconBytes(item.IconPath);

                        // 分支注释：如果成功提取到图标字节数组，回到 UI 线程加载 BitmapImage
                        if (pngBytes != null && pngBytes.Length > 0)
                        {
                            DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var bitmapImage = new BitmapImage();
                                    var randomAccessStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                                    using (var writer = new Windows.Storage.Streams.DataWriter(randomAccessStream.GetOutputStreamAt(0)))
                                    {
                                        writer.WriteBytes(pngBytes);
                                        await writer.StoreAsync();
                                        await writer.FlushAsync();
                                    }
                                    await bitmapImage.SetSourceAsync(randomAccessStream);
                                    item.IconImage = bitmapImage;
                                }
                                catch (Exception ex)
                                {
                                    Serilog.Log.Error(ex, "UI 线程加载图标 BitmapImage 发生异常，对应路径: {Path}", item.IconPath);
                                }
                            });
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 计算三个列表的总条目并更新标题文本。
        /// </summary>
        private void UpdateCountText()
        {
            int total = _fileItems.Count + _directoryItems.Count + _backgroundItems.Count;
            CountText.Text = $"({total} 个菜单项)";
        }

        /// <summary>
        /// 页签选择项变更时的处理。
        /// </summary>
        private void ScopePivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 在页签切换时，直接保留已有的内存加载，不需要在此刻重新刷新数据库
        }

        /// <summary>
        /// 点击管理员提权运行按钮时的处理。
        /// </summary>
        private void ElevateButton_Click(object sender, RoutedEventArgs e)
        {
            _service.ElevateAndRestart();
        }

        /// <summary>
        /// 点击新增自定义菜单按钮时的处理。
        /// </summary>
        private async void AddCustom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContextMenuEditorDialog
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            // 分支注释：如果用户在弹窗中确认创建，则调用服务添加自定义注册表键
            if (result == ContentDialogResult.Primary)
            {
                bool success = _service.AddCustomMenuItem(dialog.MenuName, dialog.MenuCommand, dialog.MenuScope, dialog.MenuIcon);
                // 分支注释：如果创建成功则重新载入，否则显示错误弹窗
                if (success)
                {
                    RefreshAllData();
                }
                else
                {
                    await ShowErrorDialog("创建自定义右键菜单失败，请检查是否被安全软件拦截或权限不足。");
                }
            }
        }

        /// <summary>
        /// 切换状态开关 (ToggleSwitch) 的事件处理。
        /// </summary>
        private async void ActiveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // 分支注释：只在交互切换状态时触发，排除初始化绑定时触发的逻辑
            if (sender is ToggleSwitch toggle && toggle.DataContext is ContextMenuItem item)
            {
                // 分支注释：如果当前界面状态与数据实体状态不一致，才调用注册表修改
                if (item.IsActive != toggle.IsOn)
                {
                    bool success = _service.ToggleMenuItem(item, toggle.IsOn);
                    // 分支注释：如果修改失败，回滚 ToggleSwitch 的界面表现并提示
                    if (!success)
                    {
                        // 临时解除绑定以避免事件回环
                        toggle.Toggled -= ActiveToggle_Toggled;
                        toggle.IsOn = item.IsActive;
                        toggle.Toggled += ActiveToggle_Toggled;
                        await ShowErrorDialog("修改状态失败，请确认是否具有对应注册表的写权限或以管理员身份运行。");
                    }
                    else
                    {
                        item.IsActive = toggle.IsOn;
                    }
                }
            }
        }

        /// <summary>
        /// 点击删除自定义菜单按钮时的处理。
        /// </summary>
        private async void DeleteCustom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ContextMenuItem item)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要彻底删除自定义菜单项 “{item.DisplayName}” 吗？\n警告：删除后将永久无法恢复该右键菜单项！",
                    PrimaryButtonText = "确认删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

                var res = await confirmDialog.ShowAsync();
                // 分支注释：如果确认删除，调用 service 删除，并更新列表
                if (res == ContentDialogResult.Primary)
                {
                    bool success = _service.DeleteCustomMenuItem(item);
                    // 分支注释：如果删除成功，重新刷新界面，否则报错
                    if (success)
                    {
                        RefreshAllData();
                    }
                    else
                    {
                        await ShowErrorDialog("删除菜单项失败，请确认是否具有管理员权限。");
                    }
                }
            }
        }

        /// <summary>
        /// 弹出错误消息通用对话框。
        /// </summary>
        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "操作提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        /// <summary>
        /// Windows 11 经典右键菜单样式开关切换的处理事件。
        /// </summary>
        private void ClassicStyleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                bool success = _service.SetClassicMenu(toggle.IsOn);
                // 分支注释：若修改成功，同步更新静态变量并重新刷新整个数据列表
                if (success)
                {
                    IsClassicMenuStatic = toggle.IsOn;
                    RefreshAllData();
                }
                else
                {
                    // 若失败，则弹窗提示并将开关状态复位
                    toggle.Toggled -= ClassicStyleToggle_Toggled;
                    toggle.IsOn = !toggle.IsOn;
                    toggle.Toggled += ClassicStyleToggle_Toggled;
                }
            }
        }
    }
}
