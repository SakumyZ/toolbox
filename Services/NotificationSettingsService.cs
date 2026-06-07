using System;
using System.IO;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// 通知设置服务，负责通知样式、主题、自动关闭时长的持久化。
    /// </summary>
    public class NotificationSettingsService
    {
        private const string DisplayStyleKey = "notification_display_style";
        private const string ColorThemeKey = "notification_color_theme";
        private const string AutoCloseMillisecondsKey = "notification_auto_close_ms";

        private readonly string _connectionString;

        public NotificationSettingsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);

            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";

            InitializeTables();
            EnsureDefaultSettings();
        }

        /// <summary>
        /// 获取当前通知设置。
        /// </summary>
        public NotificationSettings GetSettings()
        {
            Console.WriteLine("[NotificationSettingsService] Loading settings...");
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var displayStyleStr = GetSettingValue(connection, DisplayStyleKey, NotificationDisplayStyle.Standard.ToString());
            var colorThemeStr = GetSettingValue(connection, ColorThemeKey, NotificationColorTheme.System.ToString());
            var autoCloseMsStr = GetSettingValue(connection, AutoCloseMillisecondsKey, NotificationAutoCloseDurations.Default.ToString());

            var settings = new NotificationSettings
            {
                DisplayStyle = ParseEnum(displayStyleStr, NotificationDisplayStyle.Standard),
                ColorTheme = ParseEnum(colorThemeStr, NotificationColorTheme.System),
                AutoCloseMilliseconds = NormalizeAutoCloseMilliseconds(int.TryParse(autoCloseMsStr, out var ms) ? ms : NotificationAutoCloseDurations.Default)
            };
            Console.WriteLine($"[NotificationSettingsService] Loaded settings: DisplayStyle={settings.DisplayStyle}, ColorTheme={settings.ColorTheme}, AutoClose={settings.AutoCloseMilliseconds}ms");
            return settings;
        }

        /// <summary>
        /// 保存通知设置。
        /// </summary>
        public void SaveSettings(NotificationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Console.WriteLine($"[NotificationSettingsService] Saving settings: DisplayStyle={settings.DisplayStyle}, ColorTheme={settings.ColorTheme}, AutoClose={settings.AutoCloseMilliseconds}ms");
            var normalizedMs = NormalizeAutoCloseMilliseconds(settings.AutoCloseMilliseconds);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            SaveSettingValue(connection, DisplayStyleKey, settings.DisplayStyle.ToString());
            SaveSettingValue(connection, ColorThemeKey, settings.ColorTheme.ToString());
            SaveSettingValue(connection, AutoCloseMillisecondsKey, normalizedMs.ToString());
        }

        /// <summary>
        /// 规范化自动关闭毫秒值，确保在允许范围内。
        /// </summary>
        public int NormalizeAutoCloseMilliseconds(int milliseconds)
        {
            if (milliseconds < NotificationAutoCloseDurations.Minimum)
            {
                return NotificationAutoCloseDurations.Default;
            }

            if (milliseconds > NotificationAutoCloseDurations.Maximum)
            {
                return NotificationAutoCloseDurations.Maximum;
            }

            return milliseconds;
        }

        private void InitializeTables()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    SettingKey TEXT PRIMARY KEY,
                    SettingValue TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();
        }

        private void EnsureDefaultSettings()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // 确保存在默认值
            if (string.IsNullOrWhiteSpace(GetSettingValue(connection, DisplayStyleKey, null)))
            {
                SaveSettingValue(connection, DisplayStyleKey, NotificationDisplayStyle.Standard.ToString());
            }

            if (string.IsNullOrWhiteSpace(GetSettingValue(connection, ColorThemeKey, null)))
            {
                SaveSettingValue(connection, ColorThemeKey, NotificationColorTheme.System.ToString());
            }

            if (string.IsNullOrWhiteSpace(GetSettingValue(connection, AutoCloseMillisecondsKey, null)))
            {
                SaveSettingValue(connection, AutoCloseMillisecondsKey, NotificationAutoCloseDurations.Default.ToString());
            }
        }

        private string GetSettingValue(SqliteConnection connection, string key, string? defaultValue)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = @key";
                command.Parameters.AddWithValue("@key", key);

                var result = command.ExecuteScalar();
                return result is string value ? value : (defaultValue ?? string.Empty);
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"[NotificationSettingsService] GetSettingValue SQLite error for key '{key}': {ex}");
                try
                {
                    var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox");
                    Directory.CreateDirectory(appData);
                    var logPath = Path.Combine(appData, "nav_error.txt");
                    File.AppendAllText(logPath, DateTime.Now.ToString("o") + "\nGetSettingValue SQLite error: " + ex.ToString() + "\n\n");
                }
                catch { }

                return defaultValue ?? string.Empty;
            }
        }

        private void SaveSettingValue(SqliteConnection connection, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt) VALUES (@key, @value, @updatedAt)
                ON CONFLICT(SettingKey) DO UPDATE SET SettingValue = @value, UpdatedAt = @updatedAt
            ";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o"));

            command.ExecuteNonQuery();
        }

        private static T ParseEnum<T>(string? value, T defaultValue) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return Enum.TryParse<T>(value, out var result) ? result : defaultValue;
        }
    }
}