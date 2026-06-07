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
using Serilog;

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
        /// 主窗口实例，供 FileOpenPicker 等需要窗口句柄 hometown 的 API 使用
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
            
            // 初始化日志引擎
            LogService.Initialize();

            // 注册全局异常以便尽早记录问题
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var exObj = e.ExceptionObject as Exception;
                Log.Fatal(exObj, "未捕获的全局异常: {Message}", exObj?.Message ?? "未知错误");
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Fatal(e.Exception, "未观察到的 Task 异常: {Message}", e.Exception?.Message);
                e.SetObserved();
            };

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "InitializeComponent 崩溃");
                throw;
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Log.Information("应用程序启动中 (OnLaunched)...");

            // 单实例检测
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                Log.Warning("检测到已有 ToolBox 实例运行，激活现有窗口并退出。");
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

                // 数据库和服务就绪后，从 AppSettings 载入用户设置的日志等级
                LogService.LoadLevelFromSettings();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainWindow 启动或依赖服务启动失败");
                throw;
            }

            _window.Closed += (sender, eventArgs) =>
            {
                Log.Information("主窗口已关闭，正在卸载服务并释放资源...");
                ReminderSchedulerService.Instance.Dispose();
                _mutex?.Dispose();
                _mutex = null;
                LogService.CloseAndFlush();
            };
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