using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// SSH config 预设与文件切换服务。
    /// </summary>
    public class SshConfigService
    {
        private readonly string _connectionString;
        private readonly string _configPath;
        private readonly string _sshDirectory;

        public SshConfigService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _sshDirectory = Path.Combine(homePath, ".ssh");
            _configPath = Path.Combine(_sshDirectory, "config");

            InitializeTables();
        }

        /// <summary>
        /// 当前 SSH config 路径。
        /// </summary>
        public string ConfigPath => _configPath;

        private void InitializeTables()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS SshConfigPresets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Description TEXT NOT NULL DEFAULT '',
                    Content TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 0,
                    LastUsedAt TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_SshConfigPresets_IsActive ON SshConfigPresets(IsActive);
            ";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取全部预设。
        /// </summary>
        public List<SshConfigPreset> GetAllPresets()
        {
            var presets = new List<SshConfigPreset>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, Description, Content, IsActive, LastUsedAt, CreatedAt, UpdatedAt
                FROM SshConfigPresets
                ORDER BY IsActive DESC, Name ASC";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                presets.Add(new SshConfigPreset
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    Content = reader.GetString(3),
                    IsActive = reader.GetInt32(4) == 1,
                    LastUsedAt = ParseNullableDateTime(reader.IsDBNull(5) ? null : reader.GetString(5)),
                    CreatedAt = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                    UpdatedAt = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind)
                });
            }

            return presets;
        }

        /// <summary>
        /// 获取单个预设。
        /// </summary>
        public SshConfigPreset? GetPreset(long presetId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, Description, Content, IsActive, LastUsedAt, CreatedAt, UpdatedAt
                FROM SshConfigPresets
                WHERE Id = $id";
            command.Parameters.AddWithValue("$id", presetId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new SshConfigPreset
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Content = reader.GetString(3),
                IsActive = reader.GetInt32(4) == 1,
                LastUsedAt = ParseNullableDateTime(reader.IsDBNull(5) ? null : reader.GetString(5)),
                CreatedAt = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind)
            };
        }

        /// <summary>
        /// 保存预设。
        /// </summary>
        public (bool success, string message, long presetId) SavePreset(SshConfigPreset preset)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");

            try
            {
                if (preset.Id <= 0)
                {
                    var insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = @"
                        INSERT INTO SshConfigPresets (Name, Description, Content, IsActive, LastUsedAt, CreatedAt, UpdatedAt)
                        VALUES ($name, $description, $content, $isActive, $lastUsedAt, $createdAt, $updatedAt);
                        SELECT last_insert_rowid();";
                    BindParameters(insertCommand, preset, now, now);
                    preset.Id = (long)insertCommand.ExecuteScalar()!;
                    return (true, "预设已保存", preset.Id);
                }

                var updateCommand = connection.CreateCommand();
                updateCommand.CommandText = @"
                    UPDATE SshConfigPresets SET
                        Name = $name,
                        Description = $description,
                        Content = $content,
                        IsActive = $isActive,
                        LastUsedAt = $lastUsedAt,
                        UpdatedAt = $updatedAt
                    WHERE Id = $id";
                BindParameters(updateCommand, preset, preset.CreatedAt == default ? now : preset.CreatedAt.ToString("o"), now);
                updateCommand.Parameters.AddWithValue("$id", preset.Id);
                updateCommand.ExecuteNonQuery();
                return (true, "预设已更新", preset.Id);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return (false, "预设名称已存在，请换一个名称", preset.Id);
            }
        }

        /// <summary>
        /// 删除预设。
        /// </summary>
        public void DeletePreset(long presetId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SshConfigPresets WHERE Id = $id";
            command.Parameters.AddWithValue("$id", presetId);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 读取当前 config 文本。
        /// </summary>
        public string LoadCurrentConfig()
        {
            if (!File.Exists(_configPath))
            {
                return string.Empty;
            }

            return NormalizeLineEndings(File.ReadAllText(_configPath));
        }

        /// <summary>
        /// 激活指定预设并覆盖当前 config。
        /// </summary>
        public (bool success, string message) ActivatePreset(long presetId)
        {
            var preset = GetPreset(presetId);
            if (preset == null)
            {
                return (false, "未找到指定预设");
            }

            try
            {
                Directory.CreateDirectory(_sshDirectory);

                if (File.Exists(_configPath))
                {
                    var backupPath = Path.Combine(_sshDirectory,
                        $"config.backup.{DateTime.Now:yyyyMMddHHmmss}");
                    File.Copy(_configPath, backupPath, overwrite: true);
                }

                File.WriteAllText(_configPath, NormalizeLineEndings(preset.Content));

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var now = DateTime.Now.ToString("o");

                var resetCommand = connection.CreateCommand();
                resetCommand.CommandText = "UPDATE SshConfigPresets SET IsActive = 0, UpdatedAt = $updatedAt";
                resetCommand.Parameters.AddWithValue("$updatedAt", now);
                resetCommand.ExecuteNonQuery();

                var activateCommand = connection.CreateCommand();
                activateCommand.CommandText = @"
                    UPDATE SshConfigPresets
                    SET IsActive = 1, LastUsedAt = $lastUsedAt, UpdatedAt = $updatedAt
                    WHERE Id = $id";
                activateCommand.Parameters.AddWithValue("$lastUsedAt", now);
                activateCommand.Parameters.AddWithValue("$updatedAt", now);
                activateCommand.Parameters.AddWithValue("$id", presetId);
                activateCommand.ExecuteNonQuery();

                return (true, $"已切换到预设：{preset.Name}");
            }
            catch (Exception ex)
            {
                return (false, $"切换失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 为当前 config 生成导入预设。
        /// </summary>
        public SshConfigPreset CreatePresetFromCurrentConfig()
        {
            return new SshConfigPreset
            {
                Name = $"当前配置 {DateTime.Now:MM-dd HH:mm}",
                Description = "从当前 SSH config 导入",
                Content = LoadCurrentConfig(),
                IsActive = false
            };
        }

        public (bool success, string message) ValidateConfigContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return (false, "SSH config 不能为空");
            }

            if (!Regex.IsMatch(content, @"^\s*(Host|Include)\s+.+$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                return (false, "SSH config 至少需要包含一个 Host 或 Include 规则");
            }

            return (true, string.Empty);
        }

        private static DateTime? ParseNullableDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var result)
                ? result
                : null;
        }

        private static void BindParameters(SqliteCommand command, SshConfigPreset preset, string createdAt, string updatedAt)
        {
            command.Parameters.AddWithValue("$name", preset.Name);
            command.Parameters.AddWithValue("$description", preset.Description);
            command.Parameters.AddWithValue("$content", NormalizeLineEndings(preset.Content));
            command.Parameters.AddWithValue("$isActive", preset.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$lastUsedAt", preset.LastUsedAt?.ToString("o") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", createdAt);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
        }

        private static string NormalizeLineEndings(string content)
        {
            return content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }
    }
}