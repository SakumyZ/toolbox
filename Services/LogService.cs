using System;
using System.IO;
using System.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Microsoft.Data.Sqlite;

namespace ToolBox.Services
{
    /// <summary>
    /// 系统日志服务，负责 Serilog 的配置、生命周期管理以及动态日志等级切换。
    /// </summary>
    public static class LogService
    {
        private static readonly object _lock = new();

        /// <summary>
        /// 获取动态日志级别控制器。
        /// </summary>
        public static LoggingLevelSwitch LevelSwitch { get; } = new LoggingLevelSwitch();

        private static string GetLogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox",
                "logs");
        }

        /// <summary>
        /// 初始化 Serilog 日志系统。
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    var logDir = GetLogDirectory();
                    Directory.CreateDirectory(logDir);
                    var logPath = Path.Combine(logDir, "log-.txt");

                    // 默认设置为 Debug 级别，以便启动时能抓到尽量多的细节，后面会从配置中加载
                    LevelSwitch.MinimumLevel = LogEventLevel.Debug;

                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.ControlledBy(LevelSwitch)
                        .WriteTo.Debug(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                        .WriteTo.File(
                            logPath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                            encoding: System.Text.Encoding.UTF8)
                        .CreateLogger();

                    Log.Information("=== ToolBox 日志系统初始化完成 ===");
                }
                catch (Exception ex)
                {
                    // 降级使用 Console 输出，防止应用因日志引擎崩溃而无法启动
                    Console.WriteLine($"[Fatal] Failed to initialize Serilog: {ex}");
                }
            }
        }

        /// <summary>
        /// 从数据库配置中加载日志等级。
        /// </summary>
        public static void LoadLevelFromSettings()
        {
            lock (_lock)
            {
                try
                {
                    var appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ToolBox");
                    var dbPath = Path.Combine(appDataPath, "snippets.db");
                    if (!File.Exists(dbPath))
                    {
                        Log.Warning("数据库文件不存在，使用默认日志级别：Information");
                        LevelSwitch.MinimumLevel = LogEventLevel.Information;
                        return;
                    }

                    string connectionString = $"Data Source={dbPath}";
                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'log_level'";
                    var result = command.ExecuteScalar();

                    var levelStr = result as string ?? "Information";
                    if (Enum.TryParse<LogEventLevel>(levelStr, out var level))
                    {
                        LevelSwitch.MinimumLevel = level;
                        Log.Information("从设置加载日志级别: {Level}", level);
                    }
                    else
                    {
                        LevelSwitch.MinimumLevel = LogEventLevel.Information;
                        Log.Warning("无法解析日志级别 '{LevelStr}'，使用默认值：Information", levelStr);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "加载日志级别设置时发生错误，使用默认级别：Information");
                    LevelSwitch.MinimumLevel = LogEventLevel.Information;
                }
            }
        }

        /// <summary>
        /// 更新日志等级并保存到数据库。
        /// </summary>
        /// <param name="level">目标日志等级。</param>
        public static void UpdateLogLevel(LogEventLevel level)
        {
            lock (_lock)
            {
                LevelSwitch.MinimumLevel = level;
                Log.Information("日志级别已更新为: {Level}", level);

                try
                {
                    var appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ToolBox");
                    var dbPath = Path.Combine(appDataPath, "snippets.db");
                    string connectionString = $"Data Source={dbPath}";
                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt) VALUES ('log_level', @value, @updatedAt)
                        ON CONFLICT(SettingKey) DO UPDATE SET SettingValue = @value, UpdatedAt = @updatedAt
                    ";
                    command.Parameters.AddWithValue("@value", level.ToString());
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "保存日志级别设置到数据库时失败");
                }
            }
        }

        /// <summary>
        /// 打开日志目录。
        /// </summary>
        public static void OpenLogFolder()
        {
            try
            {
                var logDir = GetLogDirectory();
                if (Directory.Exists(logDir))
                {
                    Process.Start("explorer.exe", logDir);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "打开日志文件夹失败");
            }
        }

        /// <summary>
        /// 清理所有日志文件。
        /// </summary>
        public static void ClearLogs()
        {
            try
            {
                var logDir = GetLogDirectory();
                if (Directory.Exists(logDir))
                {
                    var files = Directory.GetFiles(logDir, "log-*.txt");
                    int deletedCount = 0;
                    int failedCount = 0;
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch
                        {
                            failedCount++;
                        }
                    }
                    Log.Information("日志清理完成，成功删除 {DeletedCount} 个文件，失败 {FailedCount} 个（当前日志文件已被系统锁定）", deletedCount, failedCount);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "清理日志文件失败");
            }
        }

        /// <summary>
        /// 刷新并关闭日志。
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.Information("=== ToolBox 日志系统已关闭 ===");
            Log.CloseAndFlush();
        }
    }
}
