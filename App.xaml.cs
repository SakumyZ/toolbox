using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using System.IO;
using ToolBox.Models;
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

            // 执行数据库结构链式迁移
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ToolBox");
                Directory.CreateDirectory(appDataPath);
                var dbPath = Path.Combine(appDataPath, "snippets.db");
                var connectionString = $"Data Source={dbPath}";
                DatabaseMigrationService.Migrate(connectionString);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "数据库链式迁移发生异常");
            }

            // 注册 WinUI 框架的未捕获异常
            this.UnhandledException += (s, e) =>
            {
                var ex = e.Exception;
                string extraInfo = "";
                if (ex != null)
                {
                    extraInfo = $"HResult: 0x{ex.HResult:X8}\n" +
                                $"Source: {ex.Source}\n" +
                                $"StackTrace: {ex.StackTrace}\n" +
                                $"InnerException: {ex.InnerException}\n";
                    if (ex is Microsoft.UI.Xaml.Markup.XamlParseException xpe)
                    {
                        extraInfo += $"XamlParseException - Message: {xpe.Message}\n";
                    }
                }
                
                Log.Fatal(ex, "WinUI 未捕获异常: {Message}\n{ExtraInfo}", e.Message, extraInfo);
                Console.Error.WriteLine($"[FATAL WINUI EXCEPTION] {e.Message}\n{extraInfo}");
                LogService.CloseAndFlush();
                e.Handled = true; // 设为已处理，防止 fail-fast 崩溃
            };

            // 注册全局异常以便尽早记录问题
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var exObj = e.ExceptionObject as Exception;
                Log.Fatal(exObj, "未捕获的全局异常: {Message}", exObj?.Message ?? "未知错误");
                Console.Error.WriteLine($"[FATAL UNHANDLED EXCEPTION] {exObj}");
                LogService.CloseAndFlush();
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Fatal(e.Exception, "未观察到的 Task 异常: {Message}", e.Exception?.Message);
                Console.Error.WriteLine($"[FATAL UNOBSERVED TASK EXCEPTION] {e.Exception}");
                LogService.CloseAndFlush();
                e.SetObserved();
            };

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "InitializeComponent 崩溃");
                Console.Error.WriteLine($"[FATAL INITIALIZE COMPONENT CRASH] {ex}");
                LogService.CloseAndFlush();
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

                // 启动后台自动同步
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var backupService = new BackupService();
                        var webdav = backupService.GetWebDavSettings();
                        if (webdav.IsAutoSyncEnabled && !string.IsNullOrWhiteSpace(webdav.Url))
                        {
                            Log.Information("App OnLaunched: 正在执行 WebDAV 启动自动同步...");
                            var (result, message) = await backupService.SyncWithWebDavAsync(webdav.Url, webdav.Username, webdav.Password, webdav.Directory);
                            Log.Information("App OnLaunched: WebDAV 启动自动同步完成. 结果 = {Result}, 消息 = {Message}", result, message);
                            
                            if (result == SyncResult.Downloaded)
                            {
                                // 提示用户重启应用
                                MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                                {
                                    var ns = new NotificationService();
                                    var reminder = new Reminder
                                    {
                                        Title = "云端配置已同步",
                                        Message = "已自动从云端同步最新配置。重启应用后生效。",
                                        Category = ReminderPresets.Custom
                                    };
                                    ns.ShowReminderNotification(reminder, out _);
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "App OnLaunched: WebDAV 启动自动同步失败");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainWindow 启动或依赖服务启动失败");
                Console.Error.WriteLine($"[FATAL MAINWINDOW LAUNCH CRASH] {ex}");
                LogService.CloseAndFlush();
                throw;
            }

            _window.Closed += (sender, eventArgs) =>
            {
                Log.Information("主窗口已关闭，正在卸载服务并释放资源...");

                // 退出时执行 WebDAV 自动同步（同步上传）
                try
                {
                    var backupService = new BackupService();
                    var webdav = backupService.GetWebDavSettings();
                    if (webdav.IsAutoSyncEnabled && !string.IsNullOrWhiteSpace(webdav.Url))
                    {
                        Log.Information("App Closed: 检测到自动同步已启用，正在尝试同步本地数据到云端...");
                        var syncTask = Task.Run(async () =>
                        {
                            var activeDbPath = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "ToolBox", "snippets.db");
                            var localTime = File.GetLastWriteTime(activeDbPath);
                            
                            var remoteTime = await backupService.GetRemoteBackupLastModifiedAsync(webdav.Url, webdav.Username, webdav.Password, webdav.Directory);
                            if (remoteTime == null || localTime > remoteTime.Value.AddSeconds(2))
                            {
                                var tempZipPath = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "ToolBox", $"TempExitSyncBackup_{Guid.NewGuid():N}.zip");
                                try
                                {
                                    backupService.CreateBackupZip(tempZipPath);
                                    await backupService.UploadBackupToWebDavAsync(webdav.Url, webdav.Username, webdav.Password, webdav.Directory, tempZipPath);
                                    Log.Information("App Closed: 已自动将本地最新配置上传至云端");
                                }
                                finally
                                {
                                    if (File.Exists(tempZipPath))
                                    {
                                        File.Delete(tempZipPath);
                                    }
                                }
                            }
                            else
                            {
                                Log.Information("App Closed: 本地配置未新于云端，无需上传");
                            }
                        });

                        // 同步等待至多 6 秒
                        syncTask.Wait(TimeSpan.FromSeconds(6));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "App Closed: WebDAV 自动同步退出时发生异常");
                }

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