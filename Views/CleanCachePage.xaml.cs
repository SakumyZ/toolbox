using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ToolBox.Views
{
    /// <summary>
    /// 系统缓存清理页面
    /// </summary>
    public sealed partial class CleanCachePage : Page
    {
        public CleanCachePage()
        {
            InitializeComponent();
        }

        private async void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            CleanButton.IsEnabled = false;
            LogBox.Text = $"[INFO] Starting cleanup process at {DateTime.Now}...\n";

            // 使用相对于应用输出目录的脚本路径
            string batPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Scripts", "clear.bat");

            if (!File.Exists(batPath))
            {
                Log($"[ERROR] Script not found: {batPath}");
                CleanButton.IsEnabled = true;
                return;
            }

            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };

                process.OutputDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        DispatcherQueue.TryEnqueue(() => Log(args.Data));
                    }
                };

                process.ErrorDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        DispatcherQueue.TryEnqueue(() => Log($"[STDERR] {args.Data}"));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                DispatcherQueue.TryEnqueue(() =>
                    Log($"\n[INFO] Cleanup finished with exit code: {process.ExitCode}"));
            });

            CleanButton.IsEnabled = true;
        }

        private void Log(string message)
        {
            LogBox.Text += message + "\n";
            // 自动滚动到底部
            LogBox.SelectionStart = LogBox.Text.Length;
        }
    }
}