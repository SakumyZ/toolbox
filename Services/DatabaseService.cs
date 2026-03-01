using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// SQLite 数据库服务，负责代码片段的持久化存储
    /// </summary>
    public class DatabaseService
    {
        private static DatabaseService? _instance;
        private static readonly object _lock = new();
        private readonly string _connectionString;

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static DatabaseService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new DatabaseService();
                    }
                }
                return _instance;
            }
        }

        private DatabaseService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        /// <summary>
        /// 初始化数据库表结构
        /// </summary>
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Folders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ParentId INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Tags (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Color TEXT NOT NULL DEFAULT '#0078D4'
                );

                CREATE TABLE IF NOT EXISTS Snippets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Code TEXT NOT NULL,
                    Language TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    FolderId INTEGER NOT NULL DEFAULT 0,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    UsageCount INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS SnippetTags (
                    SnippetId INTEGER NOT NULL,
                    TagId INTEGER NOT NULL,
                    PRIMARY KEY (SnippetId, TagId),
                    FOREIGN KEY (SnippetId) REFERENCES Snippets(Id) ON DELETE CASCADE,
                    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Snippets_FolderId ON Snippets(FolderId);
                CREATE INDEX IF NOT EXISTS IX_Snippets_IsFavorite ON Snippets(IsFavorite);
                CREATE INDEX IF NOT EXISTS IX_Snippets_Language ON Snippets(Language);
                CREATE INDEX IF NOT EXISTS IX_SnippetTags_TagId ON SnippetTags(TagId);
            ";
            command.ExecuteNonQuery();
        }

        // ========== Folder CRUD ==========

        /// <summary>
        /// 获取所有文件夹
        /// </summary>
        public List<SnippetFolder> GetAllFolders()
        {
            var folders = new List<SnippetFolder>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, ParentId, SortOrder FROM Folders ORDER BY SortOrder, Name";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(new SnippetFolder
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    ParentId = reader.GetInt64(2),
                    SortOrder = reader.GetInt32(3)
                });
            }
            return folders;
        }

        /// <summary>
        /// 新增文件夹
        /// </summary>
        public long InsertFolder(SnippetFolder folder)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Folders (Name, ParentId, SortOrder)
                VALUES ($name, $parentId, $sortOrder);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$name", folder.Name);
            command.Parameters.AddWithValue("$parentId", folder.ParentId);
            command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);

            return (long)command.ExecuteScalar()!;
        }

        /// <summary>
        /// 更新文件夹
        /// </summary>
        public void UpdateFolder(SnippetFolder folder)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Folders SET Name = $name, ParentId = $parentId, SortOrder = $sortOrder
                WHERE Id = $id";
            command.Parameters.AddWithValue("$name", folder.Name);
            command.Parameters.AddWithValue("$parentId", folder.ParentId);
            command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
            command.Parameters.AddWithValue("$id", folder.Id);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 删除文件夹（子片段移到根目录）
        /// </summary>
        public void DeleteFolder(long folderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // 将该文件夹下的片段移到根目录
            var moveCmd = connection.CreateCommand();
            moveCmd.CommandText = "UPDATE Snippets SET FolderId = 0 WHERE FolderId = $folderId";
            moveCmd.Parameters.AddWithValue("$folderId", folderId);
            moveCmd.ExecuteNonQuery();

            // 将子文件夹移到根目录
            var moveSubCmd = connection.CreateCommand();
            moveSubCmd.CommandText = "UPDATE Folders SET ParentId = 0 WHERE ParentId = $folderId";
            moveSubCmd.Parameters.AddWithValue("$folderId", folderId);
            moveSubCmd.ExecuteNonQuery();

            // 删除文件夹
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Folders WHERE Id = $folderId";
            deleteCmd.Parameters.AddWithValue("$folderId", folderId);
            deleteCmd.ExecuteNonQuery();
        }

        // ========== Tag CRUD ==========

        /// <summary>
        /// 获取所有标签
        /// </summary>
        public List<Tag> GetAllTags()
        {
            var tags = new List<Tag>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Color FROM Tags ORDER BY Name";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(new Tag
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
            return tags;
        }

        /// <summary>
        /// 新增标签
        /// </summary>
        public long InsertTag(Tag tag)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR IGNORE INTO Tags (Name, Color) VALUES ($name, $color);
                SELECT Id FROM Tags WHERE Name = $name;";
            command.Parameters.AddWithValue("$name", tag.Name);
            command.Parameters.AddWithValue("$color", tag.Color);

            return (long)command.ExecuteScalar()!;
        }

        /// <summary>
        /// 删除标签
        /// </summary>
        public void DeleteTag(long tagId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "DELETE FROM SnippetTags WHERE TagId = $tagId";
            cmd1.Parameters.AddWithValue("$tagId", tagId);
            cmd1.ExecuteNonQuery();

            var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "DELETE FROM Tags WHERE Id = $tagId";
            cmd2.Parameters.AddWithValue("$tagId", tagId);
            cmd2.ExecuteNonQuery();
        }

        // ========== Snippet CRUD ==========

        /// <summary>
        /// 获取所有片段（可选筛选条件）
        /// </summary>
        public List<Snippet> GetSnippets(long? folderId = null, string? searchText = null,
            string? language = null, long? tagId = null, bool? isFavorite = null,
            string orderBy = "UpdatedAt DESC")
        {
            var snippets = new List<Snippet>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var conditions = new List<string>();
            var command = connection.CreateCommand();

            // 基本查询
            var sql = "SELECT DISTINCT s.Id, s.Title, s.Code, s.Language, s.Description, " +
                      "s.FolderId, s.IsFavorite, s.UsageCount, s.CreatedAt, s.UpdatedAt " +
                      "FROM Snippets s ";

            if (tagId.HasValue)
            {
                sql += "INNER JOIN SnippetTags st ON s.Id = st.SnippetId ";
                conditions.Add("st.TagId = $tagId");
                command.Parameters.AddWithValue("$tagId", tagId.Value);
            }

            if (folderId.HasValue)
            {
                conditions.Add("s.FolderId = $folderId");
                command.Parameters.AddWithValue("$folderId", folderId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                conditions.Add("(s.Title LIKE $search OR s.Code LIKE $search OR s.Description LIKE $search)");
                command.Parameters.AddWithValue("$search", $"%{searchText}%");
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                conditions.Add("s.Language = $language");
                command.Parameters.AddWithValue("$language", language);
            }

            if (isFavorite.HasValue)
            {
                conditions.Add("s.IsFavorite = $isFavorite");
                command.Parameters.AddWithValue("$isFavorite", isFavorite.Value ? 1 : 0);
            }

            if (conditions.Count > 0)
            {
                sql += "WHERE " + string.Join(" AND ", conditions) + " ";
            }

            // 收藏置顶 + 自定义排序
            sql += $"ORDER BY s.IsFavorite DESC, s.{orderBy}";

            command.CommandText = sql;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                snippets.Add(new Snippet
                {
                    Id = reader.GetInt64(0),
                    Title = reader.GetString(1),
                    Code = reader.GetString(2),
                    Language = reader.GetString(3),
                    Description = reader.GetString(4),
                    FolderId = reader.GetInt64(5),
                    IsFavorite = reader.GetInt32(6) == 1,
                    UsageCount = reader.GetInt32(7),
                    CreatedAt = DateTime.Parse(reader.GetString(8)),
                    UpdatedAt = DateTime.Parse(reader.GetString(9))
                });
            }

            // 填充标签
            foreach (var snippet in snippets)
            {
                snippet.Tags = GetSnippetTagsString(connection, snippet.Id);
            }

            return snippets;
        }

        /// <summary>
        /// 获取片段关联的标签名称（逗号分隔）
        /// </summary>
        private string GetSnippetTagsString(SqliteConnection connection, long snippetId)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT t.Name FROM Tags t
                INNER JOIN SnippetTags st ON t.Id = st.TagId
                WHERE st.SnippetId = $snippetId
                ORDER BY t.Name";
            command.Parameters.AddWithValue("$snippetId", snippetId);

            var tags = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(reader.GetString(0));
            }
            return string.Join(", ", tags);
        }

        /// <summary>
        /// 获取片段关联的标签 ID 列表
        /// </summary>
        public List<long> GetSnippetTagIds(long snippetId)
        {
            var tagIds = new List<long>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT TagId FROM SnippetTags WHERE SnippetId = $snippetId";
            command.Parameters.AddWithValue("$snippetId", snippetId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tagIds.Add(reader.GetInt64(0));
            }
            return tagIds;
        }

        /// <summary>
        /// 新增代码片段
        /// </summary>
        public long InsertSnippet(Snippet snippet, List<long>? tagIds = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Snippets (Title, Code, Language, Description, FolderId, IsFavorite, UsageCount, CreatedAt, UpdatedAt)
                VALUES ($title, $code, $language, $description, $folderId, $isFavorite, 0, $now, $now);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$title", snippet.Title);
            command.Parameters.AddWithValue("$code", snippet.Code);
            command.Parameters.AddWithValue("$language", snippet.Language);
            command.Parameters.AddWithValue("$description", snippet.Description);
            command.Parameters.AddWithValue("$folderId", snippet.FolderId);
            command.Parameters.AddWithValue("$isFavorite", snippet.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("$now", now);

            var snippetId = (long)command.ExecuteScalar()!;

            // 关联标签
            if (tagIds != null)
            {
                SetSnippetTags(connection, snippetId, tagIds);
            }

            return snippetId;
        }

        /// <summary>
        /// 更新代码片段
        /// </summary>
        public void UpdateSnippet(Snippet snippet, List<long>? tagIds = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Snippets SET
                    Title = $title, Code = $code, Language = $language,
                    Description = $description, FolderId = $folderId,
                    IsFavorite = $isFavorite, UpdatedAt = $now
                WHERE Id = $id";
            command.Parameters.AddWithValue("$title", snippet.Title);
            command.Parameters.AddWithValue("$code", snippet.Code);
            command.Parameters.AddWithValue("$language", snippet.Language);
            command.Parameters.AddWithValue("$description", snippet.Description);
            command.Parameters.AddWithValue("$folderId", snippet.FolderId);
            command.Parameters.AddWithValue("$isFavorite", snippet.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$id", snippet.Id);
            command.ExecuteNonQuery();

            // 更新标签
            if (tagIds != null)
            {
                SetSnippetTags(connection, snippet.Id, tagIds);
            }
        }

        /// <summary>
        /// 删除代码片段
        /// </summary>
        public void DeleteSnippet(long snippetId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "DELETE FROM SnippetTags WHERE SnippetId = $id";
            cmd1.Parameters.AddWithValue("$id", snippetId);
            cmd1.ExecuteNonQuery();

            var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "DELETE FROM Snippets WHERE Id = $id";
            cmd2.Parameters.AddWithValue("$id", snippetId);
            cmd2.ExecuteNonQuery();
        }

        /// <summary>
        /// 切换收藏状态
        /// </summary>
        public void ToggleFavorite(long snippetId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Snippets SET IsFavorite = CASE WHEN IsFavorite = 1 THEN 0 ELSE 1 END,
                UpdatedAt = $now WHERE Id = $id";
            command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$id", snippetId);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 增加使用次数
        /// </summary>
        public void IncrementUsageCount(long snippetId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Snippets SET UsageCount = UsageCount + 1, UpdatedAt = $now
                WHERE Id = $id";
            command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$id", snippetId);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取所有不同的编程语言
        /// </summary>
        public List<string> GetDistinctLanguages()
        {
            var languages = new List<string>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT Language FROM Snippets WHERE Language != '' ORDER BY Language";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                languages.Add(reader.GetString(0));
            }
            return languages;
        }

        /// <summary>
        /// 设置片段的标签关联
        /// </summary>
        private void SetSnippetTags(SqliteConnection connection, long snippetId, List<long> tagIds)
        {
            // 清除已有关联
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM SnippetTags WHERE SnippetId = $snippetId";
            deleteCmd.Parameters.AddWithValue("$snippetId", snippetId);
            deleteCmd.ExecuteNonQuery();

            // 重新建立关联
            foreach (var tagId in tagIds)
            {
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO SnippetTags (SnippetId, TagId) VALUES ($snippetId, $tagId)";
                insertCmd.Parameters.AddWithValue("$snippetId", snippetId);
                insertCmd.Parameters.AddWithValue("$tagId", tagId);
                insertCmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 导出所有数据为 JSON 格式的原始数据
        /// </summary>
        public (List<Snippet> Snippets, List<SnippetFolder> Folders, List<Tag> Tags) ExportAll()
        {
            return (
                GetSnippets(),
                GetAllFolders(),
                GetAllTags()
            );
        }

        /// <summary>
        /// 获取片段总数
        /// </summary>
        public int GetSnippetCount()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Snippets";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }
}
