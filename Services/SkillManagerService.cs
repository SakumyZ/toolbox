using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// Skill 管理服务，负责扫描目录、持久化元数据以及切换启用状态。
    /// </summary>
    public class SkillManagerService
    {
        private const string ActivePathKey = "skill_manager_active_path";
        private const string InactivePathKey = "skill_manager_inactive_path";

        private readonly string _connectionString;
        private readonly string _defaultActiveSkillsPath;
        private readonly string _defaultInactiveSkillsPath;

        public SkillManagerService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);

            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _defaultActiveSkillsPath = Path.Combine(homePath, ".agents", "skills");
            _defaultInactiveSkillsPath = @"D:\Backup\AI\skills";

            InitializeTables();
            EnsureDefaultSettings();
        }

        /// <summary>
        /// 获取当前 skill 目录配置。
        /// </summary>
        public SkillManagerSettings GetSettings()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            return new SkillManagerSettings
            {
                ActiveSkillsPath = GetSettingValue(connection, ActivePathKey, _defaultActiveSkillsPath),
                InactiveSkillsPath = GetSettingValue(connection, InactivePathKey, _defaultInactiveSkillsPath)
            };
        }

        /// <summary>
        /// 保存 skill 目录配置。
        /// </summary>
        public (bool success, string message) SaveSettings(SkillManagerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ActiveSkillsPath))
            {
                return (false, "启用目录不能为空");
            }

            if (string.IsNullOrWhiteSpace(settings.InactiveSkillsPath))
            {
                return (false, "停用目录不能为空");
            }

            var normalizedActivePath = NormalizePath(settings.ActiveSkillsPath);
            var normalizedInactivePath = NormalizePath(settings.InactiveSkillsPath);

            // 如果两个目录相同，则无法区分启停状态。
            if (string.Equals(normalizedActivePath, normalizedInactivePath, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "启用目录和停用目录不能相同");
            }

            Directory.CreateDirectory(normalizedActivePath);
            Directory.CreateDirectory(normalizedInactivePath);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            SaveSettingValue(connection, ActivePathKey, normalizedActivePath);
            SaveSettingValue(connection, InactivePathKey, normalizedInactivePath);

            return (true, "目录配置已保存");
        }

        /// <summary>
        /// 获取全部 skill 列表。
        /// </summary>
        public List<SkillItem> GetAllSkills()
        {
            var settings = GetSettings();
            var metadataMap = GetMetadataMap();
            var skills = new Dictionary<string, SkillItem>(StringComparer.OrdinalIgnoreCase);

            LoadSkillsFromDirectory(settings.ActiveSkillsPath, true, metadataMap, skills);
            LoadSkillsFromDirectory(settings.InactiveSkillsPath, false, metadataMap, skills);

            return skills.Values
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 保存 skill 的别名与分类。
        /// </summary>
        public (bool success, string message) SaveSkillMetadata(string skillId, string alias, string category)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return (false, "Skill 标识不能为空");
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO SkillMetadata (SkillId, Alias, Category, CreatedAt, UpdatedAt)
                VALUES ($skillId, $alias, $category, $createdAt, $updatedAt)
                ON CONFLICT(SkillId) DO UPDATE SET
                    Alias = excluded.Alias,
                    Category = excluded.Category,
                    UpdatedAt = excluded.UpdatedAt;";
            command.Parameters.AddWithValue("$skillId", skillId.Trim());
            command.Parameters.AddWithValue("$alias", alias.Trim());
            command.Parameters.AddWithValue("$category", category.Trim());
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();

            return (true, "Skill 信息已保存");
        }

        /// <summary>
        /// 切换 skill 启用状态。
        /// </summary>
        public (bool success, string message) SetSkillActive(string skillId, bool isActive)
        {
            var settings = GetSettings();
            var sourceRoot = isActive ? settings.InactiveSkillsPath : settings.ActiveSkillsPath;
            var targetRoot = isActive ? settings.ActiveSkillsPath : settings.InactiveSkillsPath;

            var sourcePath = Path.Combine(sourceRoot, skillId);
            var targetPath = Path.Combine(targetRoot, skillId);

            // 如果源目录不存在，则说明状态已发生变化或 skill 不存在。
            if (!Directory.Exists(sourcePath))
            {
                return (false, "未找到对应的 skill 目录，请先刷新列表");
            }

            Directory.CreateDirectory(targetRoot);

            // 如果目标目录已存在，则不能直接覆盖，避免误伤用户已有文件。
            if (Directory.Exists(targetPath))
            {
                return (false, $"目标目录已存在：{targetPath}");
            }

            try
            {
                // 如果源目录和目标目录在同一盘符，则直接移动即可。
                if (HasSameVolumeRoot(sourcePath, targetPath))
                {
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    // 如果跨盘移动，则改为复制后删除源目录，避免 Directory.Move 抛异常。
                    CopyDirectory(sourcePath, targetPath);
                    Directory.Delete(sourcePath, recursive: true);
                }

                return (true, isActive ? "Skill 已启用" : "Skill 已停用");
            }
            catch (Exception ex)
            {
                // 如果复制过程中产生了半成品目录，则尝试回滚清理。
                if (Directory.Exists(targetPath))
                {
                    TryDeleteDirectory(targetPath);
                }

                return (false, $"切换失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取分类列表。
        /// </summary>
        public List<string> GetAllCategories()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var categories = new List<string> { "全部分类" };
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DISTINCT Category
                FROM SkillMetadata
                WHERE TRIM(Category) <> ''
                ORDER BY Category COLLATE NOCASE ASC";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(reader.GetString(0));
            }

            return categories;
        }

        /// <summary>
        /// 初始化表结构。
        /// </summary>
        private void InitializeTables()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    SettingKey TEXT PRIMARY KEY,
                    SettingValue TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS SkillMetadata (
                    SkillId TEXT PRIMARY KEY,
                    Alias TEXT NOT NULL DEFAULT '',
                    Category TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_SkillMetadata_Category ON SkillMetadata(Category);
            ";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 写入默认配置，避免首次打开时没有目录配置。
        /// </summary>
        private void EnsureDefaultSettings()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            SaveSettingValue(connection, ActivePathKey, _defaultActiveSkillsPath, onlyIfMissing: true);
            SaveSettingValue(connection, InactivePathKey, _defaultInactiveSkillsPath, onlyIfMissing: true);
        }

        /// <summary>
        /// 获取元数据映射。
        /// </summary>
        private Dictionary<string, SkillMetadata> GetMetadataMap()
        {
            var map = new Dictionary<string, SkillMetadata>(StringComparer.OrdinalIgnoreCase);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT SkillId, Alias, Category, CreatedAt, UpdatedAt
                FROM SkillMetadata";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = new SkillMetadata
                {
                    SkillId = reader.GetString(0),
                    Alias = reader.GetString(1),
                    Category = reader.GetString(2),
                    CreatedAt = DateTime.Parse(reader.GetString(3)),
                    UpdatedAt = DateTime.Parse(reader.GetString(4))
                };
            }

            return map;
        }

        /// <summary>
        /// 从指定目录读取 skill 文件夹。
        /// </summary>
        private static void LoadSkillsFromDirectory(
            string rootPath,
            bool isActive,
            IReadOnlyDictionary<string, SkillMetadata> metadataMap,
            IDictionary<string, SkillItem> skills)
        {
            // 如果目录不存在，则跳过扫描。
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(rootPath))
            {
                var skillId = Path.GetFileName(directory);

                // 如果文件夹名为空，则跳过异常目录。
                if (string.IsNullOrWhiteSpace(skillId))
                {
                    continue;
                }

                var skillFilePath = Path.Combine(directory, "SKILL.md");
                metadataMap.TryGetValue(skillId, out var metadata);
                var description = ReadSkillDescription(skillFilePath);

                skills[skillId] = new SkillItem
                {
                    SkillId = skillId,
                    Alias = metadata?.Alias ?? string.Empty,
                    Category = string.IsNullOrWhiteSpace(metadata?.Category) ? "未分类" : metadata.Category,
                    DisplayName = string.IsNullOrWhiteSpace(metadata?.Alias) ? skillId : metadata.Alias,
                    Description = string.IsNullOrWhiteSpace(description) ? "未读取到描述" : description,
                    IsActive = isActive,
                    CurrentPath = directory,
                    SkillFilePath = skillFilePath
                };
            }
        }

        /// <summary>
        /// 从 SKILL.md 读取描述。
        /// </summary>
        private static string ReadSkillDescription(string skillFilePath)
        {
            // 如果说明文件不存在，则直接返回空。
            if (!File.Exists(skillFilePath))
            {
                return string.Empty;
            }

            try
            {
                foreach (var line in File.ReadLines(skillFilePath))
                {
                    var trimmedLine = line.Trim();

                    // 如果遇到 description frontmatter，则优先使用。
                    if (trimmedLine.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmedLine["description:".Length..].Trim().Trim('"');
                    }

                    // 如果拿到首个普通段落，则作为兜底描述。
                    if (!string.IsNullOrWhiteSpace(trimmedLine) &&
                        !trimmedLine.StartsWith("---", StringComparison.Ordinal) &&
                        !trimmedLine.StartsWith("#", StringComparison.Ordinal))
                    {
                        return trimmedLine;
                    }
                }
            }
            catch
            {
                // 如果读取失败，则保持静默并返回空描述。
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取单个配置值。
        /// </summary>
        private static string GetSettingValue(SqliteConnection connection, string key, string defaultValue)
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = $key";
            command.Parameters.AddWithValue("$key", key);

            return command.ExecuteScalar() as string ?? defaultValue;
        }

        /// <summary>
        /// 保存单个配置值。
        /// </summary>
        private static void SaveSettingValue(SqliteConnection connection, string key, string value, bool onlyIfMissing = false)
        {
            var command = connection.CreateCommand();
            command.CommandText = onlyIfMissing
                ? @"
                    INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, UpdatedAt)
                    VALUES ($key, $value, $updatedAt);"
                : @"
                    INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt)
                    VALUES ($key, $value, $updatedAt)
                    ON CONFLICT(SettingKey) DO UPDATE SET
                        SettingValue = excluded.SettingValue,
                        UpdatedAt = excluded.UpdatedAt;";

            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("o"));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 规范化路径字符串。
        /// </summary>
        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }

        /// <summary>
        /// 判断两个路径是否位于同一盘符。
        /// </summary>
        private static bool HasSameVolumeRoot(string sourcePath, string targetPath)
        {
            var sourceRoot = Path.GetPathRoot(sourcePath);
            var targetRoot = Path.GetPathRoot(targetPath);

            return string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 递归复制目录内容。
        /// </summary>
        private static void CopyDirectory(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(targetPath);

            foreach (var filePath in Directory.GetFiles(sourcePath))
            {
                var fileName = Path.GetFileName(filePath);
                var destinationFilePath = Path.Combine(targetPath, fileName);
                File.Copy(filePath, destinationFilePath, overwrite: false);
            }

            foreach (var childDirectoryPath in Directory.GetDirectories(sourcePath))
            {
                var directoryName = Path.GetFileName(childDirectoryPath);

                // 如果子目录名为空，则跳过异常项。
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    continue;
                }

                var destinationChildPath = Path.Combine(targetPath, directoryName);
                CopyDirectory(childDirectoryPath, destinationChildPath);
            }
        }

        /// <summary>
        /// 尝试删除目录，失败时保持静默。
        /// </summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                // 如果目录不存在，则无需清理。
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // 如果清理失败，则保留现场，避免覆盖原始错误信息。
            }
        }
    }
}