using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Services;
using ToolBox.Views;
using WinRT.Interop;

namespace ToolBox
{
    public sealed partial class MainWindow : Window
    {
        private readonly HotkeyService _hotkeyService = new();
        private QuickSearchWindow? _quickSearchWindow;
        private QuickAddWindow? _quickAddWindow;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            RegisterHotkeys();
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

            this.Closed += (s, e) =>
            {
                _hotkeyService.Dispose();
                RemoveWindowSubclass(hWnd, subClassProc, UIntPtr.Zero);
            };
        }

        private void OnQuickSearchRequested()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _quickSearchWindow = new QuickSearchWindow();
                _quickSearchWindow.Activate();
            });
        }

        private void OnQuickAddRequested()
        {
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
                // Navigate to settings page (placeholder)
                // ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                var selectedItem = (NavigationViewItem)args.SelectedItem;
                string pageTag = (string)selectedItem.Tag;

                switch (pageTag)
                {
                    case "Dashboard":
                        ContentFrame.Navigate(typeof(DashboardPage));
                        break;
                    case "Snippet":
                        ContentFrame.Navigate(typeof(SnippetPage));
                        break;
                    case "PortViewer":
                        ContentFrame.Navigate(typeof(PortViewerPage));
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
                    case "CleanCache":
                        ContentFrame.Navigate(typeof(CleanCachePage));
                        break;
                }
            }
        }
    }
}
