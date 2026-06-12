using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ToolBox.Models;
using Serilog;

namespace ToolBox.Services
{
    /// <summary>
    /// Skill 管理服务，负责扫描目录、持久化元数据以及切换启用状态。
    /// </summary>
    public class SkillManagerService
    {
        private const string ActivePathKey = "skill_manager_active_path";
        private const string InactivePathKey = "skill_manager_inactive_path";
        private const string AgentDirsKey = "skill_manager_agent_directories";

        private readonly string _connectionString;
        private readonly string _defaultActiveSkillsPath;
        private readonly string _defaultInactiveSkillsPath;
        private readonly List<string> _defaultAgentDirectories;

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
            _defaultAgentDirectories = new List<string>
            {
                Path.Combine(homePath, ".claude", "skills"),
                Path.Combine(homePath, ".openCode", "skills"),
                Path.Combine(homePath, ".gemini", "skills")
            };

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

            var agentDirsJson = GetSettingValue(connection, AgentDirsKey, string.Empty);
            var agentDirs = string.IsNullOrWhiteSpace(agentDirsJson) 
                ? _defaultAgentDirectories 
                : JsonSerializer.Deserialize<List<string>>(agentDirsJson) ?? _defaultAgentDirectories;

            return new SkillManagerSettings
            {
                ActiveSkillsPath = GetSettingValue(connection, ActivePathKey, _defaultActiveSkillsPath),
                InactiveSkillsPath = GetSettingValue(connection, InactivePathKey, _defaultInactiveSkillsPath),
                AgentDirectories = agentDirs
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

            Log.Information("SkillManagerService: 正在保存 Skill 目录配置 -> Active: '{ActivePath}', Inactive: '{InactivePath}'", normalizedActivePath, normalizedInactivePath);

            Directory.CreateDirectory(normalizedActivePath);
            Directory.CreateDirectory(normalizedInactivePath);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            SaveSettingValue(connection, ActivePathKey, normalizedActivePath);
            SaveSettingValue(connection, InactivePathKey, normalizedInactivePath);

            var agentDirsJson = JsonSerializer.Serialize(settings.AgentDirectories ?? new List<string>());
            SaveSettingValue(connection, AgentDirsKey, agentDirsJson);

            return (true, "目录配置已保存");
        }

        /// <summary>
        /// 获取全部 skill 列表。
        /// </summary>
        public List<SkillItem> GetAllSkills()
        {
            var settings = GetSettings();
            var metadataMap = GetMetadataMap();
            var categoryColorsMap = GetCategoryColorsMap();
            var skills = new Dictionary<string, SkillItem>(StringComparer.OrdinalIgnoreCase);

            Log.Debug("SkillManagerService: 正在从目录扫描 Skill 列表...");
            LoadSkillsFromDirectory(settings.ActiveSkillsPath, true, metadataMap, categoryColorsMap, skills);
            LoadSkillsFromDirectory(settings.InactiveSkillsPath, false, metadataMap, categoryColorsMap, skills);

            return skills.Values
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 保存 skill 的别名与分类。
        /// </summary>
        public (bool success, string message) SaveSkillMetadata(string skillId, string alias, string category, string colorHex)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return (false, "Skill 标识不能为空");
            }

            Log.Information("SkillManagerService: 正在保存 Skill 元数据 -> ID: '{SkillId}', 别名: '{Alias}', 分类: '{Category}', 颜色: '{ColorHex}'", skillId, alias, category, colorHex);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var now = DateTime.Now.ToString("o");
                var command = connection.CreateCommand();
                command.Transaction = transaction;
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

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var catCommand = connection.CreateCommand();
                    catCommand.Transaction = transaction;
                    catCommand.CommandText = @"
                        INSERT INTO CategoryMetadata (Category, ColorHex)
                        VALUES ($category, $colorHex)
                        ON CONFLICT(Category) DO UPDATE SET
                            ColorHex = excluded.ColorHex;";
                    catCommand.Parameters.AddWithValue("$category", category.Trim());
                    catCommand.Parameters.AddWithValue("$colorHex", colorHex.Trim());
                    catCommand.ExecuteNonQuery();
                }

                transaction.Commit();
                return (true, "Skill 信息已保存");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Log.Error(ex, "SkillManagerService: 保存 Skill 元数据失败");
                return (false, $"保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定分类的颜色。
        /// </summary>
        public string GetCategoryColor(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == "未分类")
            {
                return string.Empty;
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT ColorHex FROM CategoryMetadata WHERE Category = $category";
            command.Parameters.AddWithValue("$category", category.Trim());
            return command.ExecuteScalar() as string ?? string.Empty;
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

            Log.Information("SkillManagerService: 正在切换 Skill 状态 -> ID: '{SkillId}', 激活 = {IsActive}", skillId, isActive);

            // 如果源目录不存在，则说明状态已发生变化或 skill 不存在。
            if (!Directory.Exists(sourcePath))
            {
                Log.Warning("SkillManagerService: 切换状态失败，找不到源目录: {SourcePath}", sourcePath);
                return (false, "未找到对应的 skill 目录，请先刷新列表");
            }

            Directory.CreateDirectory(targetRoot);

            // 如果目标目录已存在，则不能直接覆盖，避免误伤用户已有文件。
            if (Directory.Exists(targetPath))
            {
                Log.Error("SkillManagerService: 切换状态失败，目标目录已存在: {TargetPath}", targetPath);
                return (false, $"目标目录已存在：{targetPath}");
            }

            try
            {
                // 如果源目录和目标目录在同一盘符，则直接移动即可。
                if (HasSameVolumeRoot(sourcePath, targetPath))
                {
                    Log.Debug("SkillManagerService: 在相同逻辑卷下移动目录 -> '{SourcePath}' 到 '{TargetPath}'", sourcePath, targetPath);
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    // 如果跨盘移动，则改为复制后删除源目录，避免 Directory.Move 抛异常。
                    Log.Debug("SkillManagerService: 跨盘移动目录，执行复制与删除 -> '{SourcePath}' 到 '{TargetPath}'", sourcePath, targetPath);
                    CopyDirectory(sourcePath, targetPath);
                    Directory.Delete(sourcePath, recursive: true);
                }

                // 处理软链接
                if (settings.AgentDirectories != null)
                {
                    foreach (var agentDir in settings.AgentDirectories)
                    {
                        var normalizedAgentDir = NormalizePath(agentDir);
                        var linkPath = Path.Combine(normalizedAgentDir, skillId);

                        if (isActive)
                        {
                            try
                            {
                                Directory.CreateDirectory(normalizedAgentDir);
                                if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
                                {
                                    Directory.CreateSymbolicLink(linkPath, targetPath);
                                    Log.Debug("SkillManagerService: 创建软链接成功 -> '{LinkPath}' 指向 '{TargetPath}'", linkPath, targetPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "SkillManagerService: 创建软链接失败，将尝试使用 mklink /J -> '{LinkPath}' 指向 '{TargetPath}'", linkPath, targetPath);
                                try
                                {
                                    var process = new System.Diagnostics.Process
                                    {
                                        StartInfo = new System.Diagnostics.ProcessStartInfo
                                        {
                                            FileName = "cmd.exe",
                                            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                                            UseShellExecute = false,
                                            CreateNoWindow = true
                                        }
                                    };
                                    process.Start();
                                    process.WaitForExit();
                                    if (process.ExitCode != 0)
                                    {
                                        Log.Error("SkillManagerService: mklink /J 执行失败，ExitCode = {ExitCode}", process.ExitCode);
                                    }
                                }
                                catch (Exception mkEx)
                                {
                                    Log.Error(mkEx, "SkillManagerService: 尝试 mklink /J 也失败了");
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                // Directory.Delete 可以删除目录的软链接/Junction 而不会删除源目录的内容
                                if (Directory.Exists(linkPath))
                                {
                                    // 确保它是链接而不是真实的独立文件夹（以防万一）
                                    var dirInfo = new DirectoryInfo(linkPath);
                                    if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                                    {
                                        Directory.Delete(linkPath, recursive: false);
                                        Log.Debug("SkillManagerService: 删除软链接成功 -> '{LinkPath}'", linkPath);
                                    }
                                    else
                                    {
                                        Log.Warning("SkillManagerService: 目标不是软链接，为了安全跳过删除 -> '{LinkPath}'", linkPath);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "SkillManagerService: 删除软链接失败 -> '{LinkPath}'", linkPath);
                            }
                        }
                    }
                }

                Log.Information("SkillManagerService: Skill '{SkillId}' 状态切换成功 (激活 = {IsActive})", skillId, isActive);
                return (true, isActive ? "Skill 已启用" : "Skill 已停用");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SkillManagerService: 状态切换发生异常，ID = '{SkillId}'", skillId);
                // 如果复制过程中产生了半成品目录，则尝试回滚清理。
                if (Directory.Exists(targetPath))
                {
                    TryDeleteDirectory(targetPath);
                }

                return (false, $"切换失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 归档或取消归档 Skill。
        /// </summary>
        public (bool success, string message) ArchiveSkill(string skillId, bool isArchived)
        {
            Log.Information("SkillManagerService: 正在更新归档状态 -> ID: '{SkillId}', IsArchived: {IsArchived}", skillId, isArchived);
            
            // First, if we are archiving and it is currently active, we should deactivate it.
            if (isArchived)
            {
                var skills = GetAllSkills();
                var skill = skills.FirstOrDefault(s => string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase));
                if (skill != null && skill.IsActive)
                {
                    var stopResult = SetSkillActive(skillId, false);
                    if (!stopResult.success)
                    {
                        return (false, $"归档前停用失败: {stopResult.message}");
                    }
                }
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE SkillMetadata SET IsArchived = $isArchived WHERE SkillId = $skillId";
            command.Parameters.AddWithValue("$isArchived", isArchived ? 1 : 0);
            command.Parameters.AddWithValue("$skillId", skillId);
            var rows = command.ExecuteNonQuery();

            if (rows == 0)
            {
                // If metadata doesn't exist, create it with IsArchived
                var now = DateTime.Now.ToString("o");
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO SkillMetadata (SkillId, Alias, Category, CreatedAt, UpdatedAt, IsArchived)
                    VALUES ($skillId, '', '', $createdAt, $updatedAt, $isArchived)";
                insertCmd.Parameters.AddWithValue("$skillId", skillId);
                insertCmd.Parameters.AddWithValue("$createdAt", now);
                insertCmd.Parameters.AddWithValue("$updatedAt", now);
                insertCmd.Parameters.AddWithValue("$isArchived", isArchived ? 1 : 0);
                insertCmd.ExecuteNonQuery();
            }

            return (true, isArchived ? "已归档" : "已取消归档");
        }

        /// <summary>
        /// 彻底删除 Skill。
        /// </summary>
        public (bool success, string message) DeleteSkill(string skillId)
        {
            var skills = GetAllSkills();
            var skill = skills.FirstOrDefault(s => string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase));
            if (skill == null)
            {
                return (false, "未找到该 Skill");
            }

            if (skill.IsActive)
            {
                var stopResult = SetSkillActive(skillId, false);
                if (!stopResult.success)
                {
                    return (false, $"删除前停用失败: {stopResult.message}");
                }
                
                skill.CurrentPath = Path.Combine(GetSettings().InactiveSkillsPath, skillId);
            }

            try
            {
                if (Directory.Exists(skill.CurrentPath))
                {
                    Directory.Delete(skill.CurrentPath, recursive: true);
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM SkillMetadata WHERE SkillId = $skillId";
                command.Parameters.AddWithValue("$skillId", skillId);
                command.ExecuteNonQuery();

                return (true, "已彻底删除");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SkillManagerService: 删除 Skill 失败");
                return (false, $"删除失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取分类列表。
        /// </summary>
        public List<string> GetAllCategories()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "SELECT DISTINCT Category FROM SkillMetadata WHERE TRIM(Category) <> ''";
            using (var reader = cmd1.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(reader.GetString(0));
                }
            }

            var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "SELECT Category FROM CategoryMetadata";
            using (var reader = cmd2.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(reader.GetString(0));
                }
            }

            var list = new List<string> { "全部分类" };
            list.AddRange(categories.OrderBy(c => c));
            return list;
        }

        /// <summary>
        /// 获取带颜色的完整分类列表，供管理页面使用。
        /// </summary>
        public List<(string Category, string ColorHex)> GetAllCategoriesForManagement()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "SELECT Category, ColorHex FROM CategoryMetadata";
            using (var reader1 = cmd1.ExecuteReader())
            {
                while (reader1.Read())
                {
                    dict[reader1.GetString(0)] = reader1.GetString(1);
                }
            }

            var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "SELECT DISTINCT Category FROM SkillMetadata WHERE TRIM(Category) <> ''";
            using (var reader2 = cmd2.ExecuteReader())
            {
                while (reader2.Read())
                {
                    var cat = reader2.GetString(0);
                    if (!dict.ContainsKey(cat))
                    {
                        dict[cat] = "";
                    }
                }
            }

            var categories = new List<(string, string)>();
            foreach (var kvp in dict)
            {
                if (kvp.Key != "全部分类" && kvp.Key != "未分类")
                {
                    categories.Add((kvp.Key, kvp.Value));
                }
            }
            
            return categories.OrderBy(c => c.Item1).ToList();
        }

        /// <summary>
        /// 删除分类，并将相关技能置为未分类。
        /// </summary>
        public (bool success, string message) DeleteCategory(string category)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                var cmd1 = connection.CreateCommand();
                cmd1.Transaction = transaction;
                cmd1.CommandText = "DELETE FROM CategoryMetadata WHERE Category = $category";
                cmd1.Parameters.AddWithValue("$category", category);
                cmd1.ExecuteNonQuery();

                var cmd2 = connection.CreateCommand();
                cmd2.Transaction = transaction;
                cmd2.CommandText = "UPDATE SkillMetadata SET Category = '' WHERE Category = $category";
                cmd2.Parameters.AddWithValue("$category", category);
                cmd2.ExecuteNonQuery();

                transaction.Commit();
                return (true, "已删除分类");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SkillManagerService: 删除分类失败");
                return (false, $"删除失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重命名分类，同步更新相关技能的分类标签。
        /// </summary>
        public (bool success, string message) RenameCategory(string oldCategory, string newCategory)
        {
            if (string.IsNullOrWhiteSpace(newCategory))
            {
                return (false, "分类名称不能为空");
            }
            newCategory = newCategory.Trim();

            if (string.Equals(oldCategory, newCategory, StringComparison.OrdinalIgnoreCase))
            {
                return (true, "名称未变");
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                string oldColor = "";
                var cmdColor = connection.CreateCommand();
                cmdColor.Transaction = transaction;
                cmdColor.CommandText = "SELECT ColorHex FROM CategoryMetadata WHERE Category = $oldCat";
                cmdColor.Parameters.AddWithValue("$oldCat", oldCategory);
                var result = cmdColor.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    oldColor = result.ToString() ?? "";
                }

                var cmdDel = connection.CreateCommand();
                cmdDel.Transaction = transaction;
                cmdDel.CommandText = "DELETE FROM CategoryMetadata WHERE Category = $oldCat";
                cmdDel.Parameters.AddWithValue("$oldCat", oldCategory);
                cmdDel.ExecuteNonQuery();

                if (!string.IsNullOrWhiteSpace(oldColor))
                {
                    var cmdIns = connection.CreateCommand();
                    cmdIns.Transaction = transaction;
                    cmdIns.CommandText = @"
                        INSERT INTO CategoryMetadata (Category, ColorHex)
                        VALUES ($newCat, $color)
                        ON CONFLICT(Category) DO UPDATE SET ColorHex = excluded.ColorHex";
                    cmdIns.Parameters.AddWithValue("$newCat", newCategory);
                    cmdIns.Parameters.AddWithValue("$color", oldColor);
                    cmdIns.ExecuteNonQuery();
                }

                var cmdUpd = connection.CreateCommand();
                cmdUpd.Transaction = transaction;
                cmdUpd.CommandText = "UPDATE SkillMetadata SET Category = $newCat WHERE Category = $oldCat";
                cmdUpd.Parameters.AddWithValue("$oldCat", oldCategory);
                cmdUpd.Parameters.AddWithValue("$newCat", newCategory);
                cmdUpd.ExecuteNonQuery();

                transaction.Commit();
                return (true, "已重命名分类");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SkillManagerService: 重命名分类失败");
                return (false, $"重命名失败: {ex.Message}");
            }
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

                CREATE TABLE IF NOT EXISTS CategoryMetadata (
                    Category TEXT PRIMARY KEY,
                    ColorHex TEXT NOT NULL DEFAULT ''
                );
            ";
            command.ExecuteNonQuery();

            try
            {
                var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE SkillMetadata ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;";
                alterCmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore if column already exists
            }
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
            SaveSettingValue(connection, AgentDirsKey, JsonSerializer.Serialize(_defaultAgentDirectories), onlyIfMissing: true);
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
                SELECT SkillId, Alias, Category, CreatedAt, UpdatedAt, IsArchived
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
                    UpdatedAt = DateTime.Parse(reader.GetString(4)),
                    IsArchived = reader.GetBoolean(5)
                };
            }

            return map;
        }

        /// <summary>
        /// 获取分类颜色映射。
        /// </summary>
        private Dictionary<string, string> GetCategoryColorsMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Category, ColorHex FROM CategoryMetadata";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetString(1);
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
            IReadOnlyDictionary<string, string> categoryColorsMap,
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
                
                var category = string.IsNullOrWhiteSpace(metadata?.Category) ? "未分类" : metadata.Category;
                var colorHex = categoryColorsMap.TryGetValue(category, out var hex) ? hex : string.Empty;

                skills[skillId] = new SkillItem
                {
                    SkillId = skillId,
                    Alias = metadata?.Alias ?? string.Empty,
                    Category = category,
                    CategoryColorHex = colorHex,
                    DisplayName = string.IsNullOrWhiteSpace(metadata?.Alias) ? skillId : metadata.Alias,
                    Description = string.IsNullOrWhiteSpace(description) ? "未读取到描述" : description,
                    IsActive = isActive,
                    IsArchived = metadata?.IsArchived ?? false,
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