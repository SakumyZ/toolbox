using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using System.IO;
using ToolBox.Services;
using WinRT.Interop;

namespace ToolBox
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "Global\\ToolBox_SingleInstance_Mutex";
        private Window? _window;
        private static Mutex? _mutex;

        /// <summary>
        /// 主窗口实例，供 FileOpenPicker 等需要窗口句柄的 API 使用
        /// </summary>
        public static Window? MainWindowInstance { get; private set; }

        /// <summary>
        /// 将指定窗口拉到前台并恢复（如果最小化）。
        /// </summary>
        public static void BringWindowToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            ShowWindow(hWnd, 9); // SW_RESTORE
            SetForegroundWindow(hWnd);
            SetWindowPos(hWnd, new IntPtr(0), 0, 0, 0, 0, 0x0001 | 0x0002);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        /// <summary>
        /// 查找属于另一个进程的主窗口并激活它。
        /// </summary>
        private static void ActivateExistingInstance()
        {
            int currentPid = Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                GetWindowThreadProcessId(hWnd, out int pid);
                if (pid == currentPid) return true;

                char[] buffer = new char[256];
                int len = GetWindowText(hWnd, buffer, 256);
                if (len > 0)
                {
                    string title = new string(buffer, 0, len);
                    if (title.Contains("ToolBox"))
                    {
                        BringWindowToFront(hWnd);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            // 注册全局异常以便尽早记录问题
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                TryWriteStartupLog(e.ExceptionObject?.ToString() ?? "UnhandledException");
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                TryWriteStartupLog(e.Exception?.ToString() ?? "UnobservedTaskException");
            };

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                TryWriteStartupLog(ex.ToString());
                throw;
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // ��实例检测
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // 已有实例在运行，找到它的窗口并激活
                _mutex.Dispose();
                _mutex = null;
                ActivateExistingInstance();
                Environment.Exit(0);
                return;
            }

            try
            {
                _window = new MainWindow();
                MainWindowInstance = _window;
                _window.Activate();
                InitializeDashboardProviders();
                ReminderSchedulerService.Instance.Start();
            }
            catch (Exception ex)
            {
                TryWriteStartupLog(ex.ToString());
                throw;
            }

            _window.Closed += (sender, eventArgs) =>
            {
                ReminderSchedulerService.Instance.Dispose();
                _mutex?.Dispose();
                _mutex = null;
            };
        }

        private static void TryWriteStartupLog(string content)
        {
            Console.WriteLine($"[Fatal Error] {content}");
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox");
                Directory.CreateDirectory(appData);
                var logPath = Path.Combine(appData, "startup_error.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("o") + "\n" + content + "\n\n");
            }
            catch { }
        }

        /// <summary>
        /// 注册 Dashboard 卡片提供者。
        /// </summary>
        private static void InitializeDashboardProviders()
        {
            var portFavoriteProvider = new PortFavoriteCardProvider(new PortFavoriteService());
            DashboardCardRegistry.Register(portFavoriteProvider);
        }
    }
}