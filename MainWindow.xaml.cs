using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Services;
using ToolBox.Views;
using WinRT.Interop;
using Serilog;

namespace ToolBox
{
    public sealed partial class MainWindow : Window
    {
        private readonly HotkeyService _hotkeyService = new();
        private QuickSearchWindow? _quickSearchWindow;
        private QuickAddWindow? _quickAddWindow;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                App.SetWindowIcon(this);
                ExtendsContentIntoTitleBar = true;
                RegisterHotkeys();
                // 捕获 Frame 导航失败以记录异常（用于定位 Settings 点击崩溃）
                ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
                RegisterCliPath();
                Log.Information("MainWindow 初始化成功。");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainWindow 初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 注册全局热键并 hook 消息循环
        /// </summary>
        private void RegisterHotkeys()
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            _hotkeyService.Register(hWnd);

            _hotkeyService.QuickSearchRequested += OnQuickSearchRequested;
            _hotkeyService.QuickAddRequested += OnQuickAddRequested;

            // Hook 窗口消息以接收 WM_HOTKEY
            var subClassProc = new SUBCLASSPROC(WndProc);
            _subClassProcRef = subClassProc; // 防止 GC 回收
            SetWindowSubclass(hWnd, subClassProc, UIntPtr.Zero, UIntPtr.Zero);
            Log.Debug("全局热键与窗口消息子类化 Hook 已注册。");

            this.Closed += (s, e) =>
            {
                _hotkeyService.Dispose();
                RemoveWindowSubclass(hWnd, subClassProc, UIntPtr.Zero);
                Log.Debug("主窗口已关闭，注销全局热键与 Hook。");
            };
        }

        private void OnQuickSearchRequested()
        {
            Log.Information("触发全局热键：打开快速搜索窗口。");
            DispatcherQueue.TryEnqueue(() =>
            {
                _quickSearchWindow = new QuickSearchWindow();
                _quickSearchWindow.Activate();
            });
        }

        private void OnQuickAddRequested()
        {
            Log.Information("触发全局热键：打开快速添加窗口。");
            DispatcherQueue.TryEnqueue(() =>
            {
                _quickAddWindow = new QuickAddWindow();
                _quickAddWindow.Activate();
            });
        }

        // Win32 消息子类化
        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);
        private SUBCLASSPROC? _subClassProcRef;

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            if (uMsg == (uint)HotkeyService.WM_HOTKEY)
            {
                _hotkeyService.HandleHotkeyMessage((int)wParam);
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            // Set initial page
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                try
                {
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "导航至设置页面失败");
                    return;
                }
            }
            else
            {
                var selectedItem = (NavigationViewItem)args.SelectedItem;
                string pageTag = (string)selectedItem.Tag;
                NavigateToPage(pageTag);
            }
        }

        private void ContentFrame_NavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
        {
            Log.Error(e.Exception, "框架导航失败: 目标页面 {SourcePageType}", e.SourcePageType);
        }

        /// <summary>
        /// 公共导航方法，供子页面跳转到其他模块。
        /// </summary>
        /// <param name="pageTag">目标页面的导航标签。</param>
        public void NavigateTo(string pageTag)
        {
            NavigateTo(pageTag, null);
        }

        /// <summary>
        /// 公共导航方法（带参数），供子页面跳转到其他模块并传递筛选条件。
        /// </summary>
        /// <param name="pageTag">目标页面的导航标签。</param>
        /// <param name="parameter">导航参数（如端口号）。</param>
        public void NavigateTo(string pageTag, object? parameter)
        {
            // 选中对应导航项
            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && (string)navItem.Tag == pageTag)
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
            }

            NavigateToPage(pageTag, parameter);
        }

        /// <summary>
        /// 根据 pageTag 执行 Frame 导航。
        /// </summary>
        private void NavigateToPage(string pageTag, object? parameter = null)
        {
            Log.Information("正在导航至: {PageTag}", pageTag);
            switch (pageTag)
            {
                case "Settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
                case "Dashboard":
                    ContentFrame.Navigate(typeof(DashboardPage));
                    break;
                case "Snippet":
                    ContentFrame.Navigate(typeof(SnippetPage));
                    break;
                case "PortViewer":
                    ContentFrame.Navigate(typeof(PortViewerPage), parameter);
                    break;
                case "SshConfig":
                    ContentFrame.Navigate(typeof(SshConfigPage));
                    break;
                case "SkillManager":
                    ContentFrame.Navigate(typeof(SkillManagerPage));
                    break;
                case "Reminder":
                    ContentFrame.Navigate(typeof(ReminderPage));
                    break;
                case "ServiceManager":
                    ContentFrame.Navigate(typeof(ServiceManagerPage));
                    break;
                case "ScriptManager":
                    ContentFrame.Navigate(typeof(ScriptManagerPage));
                    break;
                case "CleanCache":
                    ContentFrame.Navigate(typeof(CleanCachePage));
                    break;
                case "ContextMenuManager":
                    ContentFrame.Navigate(typeof(ContextMenuManagerPage));
                    break;
            }
        }

        /// <summary>
        /// 异步检查并自动注册当前应用 CLI 工具（t.exe）路径到系统用户级 PATH 环境中。
        /// </summary>
        private void RegisterCliPath()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                    var targetPaths = new List<string> { currentDir };

                    // 分支注释：如果是 Debug 调试环境，额外将 tools\t 的 Debug 输出目录也加入判断与注册范围
                    if (currentDir.Contains(@"\bin\Debug\"))
                    {
                        string projectRoot = Path.GetFullPath(Path.Combine(currentDir, @"..\..\..\.."));
                        string debugCliPath = Path.Combine(projectRoot, @"tools\t\bin\Debug\net8.0-windows10.0.19041.0\win-x64");
                        if (Directory.Exists(debugCliPath))
                        {
                            targetPaths.Add(debugCliPath);
                        }
                    }

                    string? userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                    userPath ??= string.Empty;

                    var existingPaths = userPath
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.TrimEnd('\\'))
                        .ToList();

                    bool needUpdate = false;
                    var pathsToAdd = new List<string>();

                    foreach (var targetPath in targetPaths)
                    {
                        // 分支注释：若用户 PATH 中不包含此路径，则添加
                        if (!existingPaths.Any(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            pathsToAdd.Add(targetPath);
                            needUpdate = true;
                        }
                    }

                    if (needUpdate)
                    {
                        string newPath = userPath;
                        foreach (var path in pathsToAdd)
                        {
                            newPath = newPath.TrimEnd(';') + ";" + path;
                            Log.Information("ToolBox CLI 自动向用户环境变量 PATH 注册了新路径: {Path}", path);
                        }
                        Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.User);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ToolBox CLI 自动注册用户 PATH 环境变量失败");
                }
            });
        }
    }
}