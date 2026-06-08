using System;
using Microsoft.Data.Sqlite;
using Serilog;

namespace ToolBox.Services
{
    /// <summary>
    /// 数据库迁移服务，负责通过 PRAGMA user_version 进行渐进式、链式数据库表结构升级。
    /// </summary>
    public static class DatabaseMigrationService
    {
        private const int TargetVersion = 2;
        private static readonly object _lock = new();

        /// <summary>
        /// 执行数据库升级迁移。
        /// </summary>
        /// <param name="connectionString">数据库连接字符串。</param>
        public static void Migrate(string connectionString)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();

                    int currentVersion = GetUserVersion(connection);
                    if (currentVersion >= TargetVersion)
                    {
                        // 即使当前数据库版本已经是目标版本，为了系统鲁棒性，依然确保 AppSettings 表存在且为最新结构
                        EnsureAppSettingsTableV2(connection, null);
                        return;
                    }

                    Log.Information("DatabaseMigrationService: 检测到数据库需要升级。当前版本: {Current}, 目标版本: {Target}", currentVersion, TargetVersion);

                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        if (currentVersion < 1)
                        {
                            // 全新安装/未记版本阶段，确保 AppSettings 表存在且为 V2 结构
                            EnsureAppSettingsTableV2(connection, transaction);
                            currentVersion = 1;
                        }

                        if (currentVersion == 1)
                        {
                            // 版本 1 -> 版本 2: 迁移 AppSettings 表，将 Key/Value 结构重构为 SettingKey/SettingValue/UpdatedAt
                            EnsureAppSettingsTableV2(connection, transaction);
                            currentVersion = 2;
                        }

                        SetUserVersion(connection, transaction, TargetVersion);
                        transaction.Commit();
                        Log.Information("DatabaseMigrationService: 数据库结构升级成功，当前版本: {Version}", TargetVersion);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Log.Error(ex, "DatabaseMigrationService: 数据库结构升级失败，已回滚。");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "DatabaseMigrationService: 升级数据库结构时发生异常。");
                }
            }
        }

        private static int GetUserVersion(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static void SetUserVersion(SqliteConnection conn, SqliteTransaction tx, int version)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA user_version = {version};";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureAppSettingsTableV2(SqliteConnection conn, SqliteTransaction? tx)
        {
            using var cmd = conn.CreateCommand();
            if (tx != null)
            {
                cmd.Transaction = tx;
            }

            // 检查表是否存在
            bool tableExists = false;
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='AppSettings';";
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    tableExists = true;
                }
            }

            if (!tableExists)
            {
                Log.Information("DatabaseMigrationService: AppSettings 表不存在，正在创建 V2 表结构...");
                cmd.CommandText = @"
                    CREATE TABLE AppSettings (
                        SettingKey TEXT PRIMARY KEY,
                        SettingValue TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();
                return;
            }

            // 如果表存在，检查是否是旧版的 Key / Value 结构
            bool isOldFormat = false;
            cmd.CommandText = "PRAGMA table_info(AppSettings)";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"] as string == "Key")
                    {
                        isOldFormat = true;
                        break;
                    }
                }
            }

            if (isOldFormat)
            {
                Log.Information("DatabaseMigrationService: 检测到旧版 AppSettings 表结构，正在迁移至 V2...");
                cmd.CommandText = "ALTER TABLE AppSettings RENAME TO AppSettings_old;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE AppSettings (
                        SettingKey TEXT PRIMARY KEY,
                        SettingValue TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt)
                    SELECT Key, Value, datetime('now', 'localtime') FROM AppSettings_old;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DROP TABLE AppSettings_old;";
                cmd.ExecuteNonQuery();
                Log.Information("DatabaseMigrationService: AppSettings V2 迁移完成。");
            }
        }
    }
}
