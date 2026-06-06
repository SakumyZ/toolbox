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
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var displayStyleStr = GetSettingValue(connection, DisplayStyleKey, NotificationDisplayStyle.Standard.ToString());
            var colorThemeStr = GetSettingValue(connection, ColorThemeKey, NotificationColorTheme.System.ToString());
            var autoCloseMsStr = GetSettingValue(connection, AutoCloseMillisecondsKey, NotificationAutoCloseDurations.Default.ToString());

            return new NotificationSettings
            {
                DisplayStyle = ParseEnum(displayStyleStr, NotificationDisplayStyle.Standard),
                ColorTheme = ParseEnum(colorThemeStr, NotificationColorTheme.System),
                AutoCloseMilliseconds = NormalizeAutoCloseMilliseconds(int.TryParse(autoCloseMsStr, out var ms) ? ms : NotificationAutoCloseDurations.Default)
            };
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
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();

            // 如果已有旧的 AppSettings 表但缺少 Value 列，尝试添加该列以兼容旧模式。
            try
            {
                using var infoCmd = connection.CreateCommand();
                infoCmd.CommandText = "PRAGMA table_info('AppSettings');";
                using var reader = infoCmd.ExecuteReader();
                bool hasValueColumn = false;
                while (reader.Read())
                {
                    // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
                    var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase))
                    {
                        hasValueColumn = true;
                        break;
                    }
                }

                if (!hasValueColumn)
                {
                    using var alterCmd = connection.CreateCommand();
                    // 添加带默认值的列以避免 NOT NULL 约束问题
                    alterCmd.CommandText = "ALTER TABLE AppSettings ADD COLUMN Value TEXT DEFAULT ''";
                    alterCmd.ExecuteNonQuery();
                }
            }
            catch (SqliteException ex)
            {
                try
                {
                    var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox");
                    Directory.CreateDirectory(appData);
                    var logPath = Path.Combine(appData, "nav_error.txt");
                    File.AppendAllText(logPath, DateTime.Now.ToString("o") + "\nInitializeTables migration error: " + ex.ToString() + "\n\n");
                }
                catch { }
            }
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
                command.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key";
                command.Parameters.AddWithValue("@key", key);

                var result = command.ExecuteScalar();
                return result is string value ? value : (defaultValue ?? string.Empty);
            }
            catch (SqliteException ex)
            {
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
                INSERT INTO AppSettings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = @value
            ";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);

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