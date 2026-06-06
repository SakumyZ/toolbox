using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Windowing;
using ToolBox.Models;
using ToolBox.Views;
using Windows.Graphics;

namespace ToolBox.Services
{
    /// <summary>
    /// 管理自定义通知窗的创建、堆叠与回收。
    /// </summary>
    public sealed class NotificationWindowManager
    {
        private const int HorizontalMargin = 24;
        private const int VerticalMargin = 24;
        private const int WindowSpacing = 12;
        private static readonly object _syncRoot = new();
        private static readonly List<NotificationWindow> _activeWindows = new();

        /// <summary>
        /// 显示一条通知。
        /// </summary>
        /// <param name="visual">通知展示内容。</param>
        public void Show(NotificationVisual visual)
        {
            var window = new NotificationWindow(visual);
            window.Closed += NotificationWindow_Closed;

            lock (_syncRoot)
            {
                _activeWindows.Add(window);
            }

            window.Activate();
            RepositionWindows();
        }

        /// <summary>
        /// 关闭当前所有通知窗。
        /// </summary>
        public void DismissAll()
        {
            NotificationWindow[] windows;

            lock (_syncRoot)
            {
                windows = _activeWindows.ToArray();
                _activeWindows.Clear();
            }

            foreach (var window in windows)
            {
                window.Closed -= NotificationWindow_Closed;
                window.Close();
            }
        }

        private void NotificationWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
        {
            if (sender is not NotificationWindow window)
            {
                return;
            }

            window.Closed -= NotificationWindow_Closed;

            lock (_syncRoot)
            {
                _activeWindows.Remove(window);
            }

            RepositionWindows();
        }

        private static void RepositionWindows()
        {
            NotificationWindow[] windows;

            lock (_syncRoot)
            {
                windows = _activeWindows.ToArray();
            }

            if (windows.Length == 0)
            {
                return;
            }

            var displayArea = GetDisplayArea(windows[^1]);
            var workArea = displayArea.WorkArea;
            var currentBottom = workArea.Y + workArea.Height - VerticalMargin;

            for (var index = windows.Length - 1; index >= 0; index--)
            {
                var window = windows[index];
                var x = workArea.X + workArea.Width - NotificationWindow.WindowWidth - HorizontalMargin;
                var y = currentBottom - window.CurrentWindowHeight;

                window.MoveTo(new PointInt32(x, y));
                currentBottom = y - WindowSpacing;
            }
        }

        private static DisplayArea GetDisplayArea(NotificationWindow fallbackWindow)
        {
            if (App.MainWindowInstance != null)
            {
                return DisplayArea.GetFromWindowId(
                    App.MainWindowInstance.AppWindow.Id,
                    DisplayAreaFallback.Primary);
            }

            return DisplayArea.GetFromWindowId(fallbackWindow.AppWindow.Id, DisplayAreaFallback.Primary);
        }
    }
}