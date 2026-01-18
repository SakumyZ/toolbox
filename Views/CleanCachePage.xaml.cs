using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ToolBox.Views
{
    public sealed partial class CleanCachePage : Page
    {
        public CleanCachePage()
        {
            InitializeComponent();
        }

        private async void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            CleanButton.IsEnabled = false;
            bool asAdmin = AdminCheckBox.IsChecked ?? false;
            LogBox.Text = $"[INFO] Starting cleanup process at {DateTime.Now}...\n";
            if (asAdmin) LogBox.Text += "[INFO] Running as Administrator (Output logs hidden)...\n";

            string batPath = @"E:\Desktop\clear.bat";

            try
            {
                if (!System.IO.File.Exists(batPath))
                {
                    Log($"[ERROR] File not found: {batPath}");
                    CleanButton.IsEnabled = true;
                    return;
                }

                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{batPath}\"",
                        UseShellExecute = asAdmin,
                        CreateNoWindow = !asAdmin,
                        Verb = asAdmin ? "runas" : ""
                    };

                    if (!asAdmin)
                    {
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
                    }

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        DispatcherQueue.TryEnqueue(() => Log("[ERROR] Failed to start process."));
                        return;
                    }

                    if (!asAdmin)
                    {
                        process.OutputDataReceived += (s, args) =>
                        {
                            if (args.Data != null)
                                DispatcherQueue.TryEnqueue(() => Log(args.Data));
                        };
                        process.ErrorDataReceived += (s, args) =>
                        {
                            if (args.Data != null)
                                DispatcherQueue.TryEnqueue(() => Log($"[STDERR] {args.Data}"));
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                    }

                    process.WaitForExit();
                });

                Log($"[INFO] Process finished.");
            }
            catch (Exception ex)
            {
                Log($"[EXCEPTION] {ex.Message}");
            }
            finally
            {
                CleanButton.IsEnabled = true;
            }
        }

        private void Log(string message)
        {
            LogBox.Text += message + "\n";
            LogBox.Select(LogBox.Text.Length, 0);
        }
    }
}
