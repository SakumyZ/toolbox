using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// 提醒配置与记录的持久化服务
    /// </summary>
    public class ReminderService
    {
        private readonly string _connectionString;

        public ReminderService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";
            InitializeTables();
        }

        private void InitializeTables()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Reminders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Category TEXT NOT NULL DEFAULT '自定义',
                    Title TEXT NOT NULL,
                    Message TEXT NOT NULL DEFAULT '',
                    TimeText TEXT NOT NULL,
                    RecurrenceType TEXT NOT NULL DEFAULT '单次',
                    IntervalMinutes INTEGER NOT NULL DEFAULT 0,
                    DayOfMonth INTEGER NOT NULL DEFAULT 1,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    LastTriggeredAt TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ReminderTriggerLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReminderId INTEGER NOT NULL,
                    TriggeredAt TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Success',
                    FOREIGN KEY (ReminderId) REFERENCES Reminders(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Reminders_IsEnabled ON Reminders(IsEnabled);
                CREATE INDEX IF NOT EXISTS IX_ReminderTriggerLogs_ReminderId ON ReminderTriggerLogs(ReminderId);
                CREATE INDEX IF NOT EXISTS IX_ReminderTriggerLogs_TriggeredAt ON ReminderTriggerLogs(TriggeredAt DESC);
            ";
            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "Reminders", "RecurrenceType", "TEXT NOT NULL DEFAULT '单次'");
            EnsureColumnExists(connection, "Reminders", "IntervalMinutes", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "Reminders", "DayOfMonth", "INTEGER NOT NULL DEFAULT 1");
        }

        /// <summary>
        /// 获取全部提醒
        /// </summary>
        public List<Reminder> GetAllReminders()
        {
            var reminders = new List<Reminder>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Category, Title, Message, TimeText, RecurrenceType, IntervalMinutes, DayOfMonth,
                       IsEnabled, LastTriggeredAt, CreatedAt, UpdatedAt
                FROM Reminders
                ORDER BY IsEnabled DESC, TimeText ASC, Title ASC";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                reminders.Add(new Reminder
                {
                    Id = reader.GetInt64(0),
                    Category = reader.GetString(1),
                    Title = reader.GetString(2),
                    Message = reader.GetString(3),
                    TimeText = reader.GetString(4),
                    RecurrenceType = reader.GetString(5),
                    IntervalMinutes = reader.GetInt32(6),
                    DayOfMonth = reader.GetInt32(7),
                    IsEnabled = reader.GetInt32(8) == 1,
                    LastTriggeredAt = ParseNullableDateTime(reader.IsDBNull(9) ? null : reader.GetString(9)),
                    CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                    UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind)
                });
            }

            return reminders;
        }

        /// <summary>
        /// 获取单个提醒
        /// </summary>
        public Reminder? GetReminder(long reminderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Category, Title, Message, TimeText, RecurrenceType, IntervalMinutes, DayOfMonth,
                       IsEnabled, LastTriggeredAt, CreatedAt, UpdatedAt
                FROM Reminders
                WHERE Id = $id";
            command.Parameters.AddWithValue("$id", reminderId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new Reminder
            {
                Id = reader.GetInt64(0),
                Category = reader.GetString(1),
                Title = reader.GetString(2),
                Message = reader.GetString(3),
                TimeText = reader.GetString(4),
                RecurrenceType = reader.GetString(5),
                IntervalMinutes = reader.GetInt32(6),
                DayOfMonth = reader.GetInt32(7),
                IsEnabled = reader.GetInt32(8) == 1,
                LastTriggeredAt = ParseNullableDateTime(reader.IsDBNull(9) ? null : reader.GetString(9)),
                CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind)
            };
        }

        /// <summary>
        /// 新增或更新提醒
        /// </summary>
        public long SaveReminder(Reminder reminder)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");
            if (reminder.Id <= 0)
            {
                var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = @"
                    INSERT INTO Reminders (Category, Title, Message, TimeText, RecurrenceType, IntervalMinutes, DayOfMonth,
                                           IsEnabled, LastTriggeredAt, CreatedAt, UpdatedAt)
                    VALUES ($category, $title, $message, $timeText, $recurrenceType, $intervalMinutes, $dayOfMonth,
                            $isEnabled, $lastTriggeredAt, $createdAt, $updatedAt);
                    SELECT last_insert_rowid();";
                BindReminderParameters(insertCommand, reminder, now, now);
                reminder.Id = (long)insertCommand.ExecuteScalar()!;
                return reminder.Id;
            }

            var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = @"
                UPDATE Reminders SET
                    Category = $category,
                    Title = $title,
                    Message = $message,
                    TimeText = $timeText,
                    RecurrenceType = $recurrenceType,
                    IntervalMinutes = $intervalMinutes,
                    DayOfMonth = $dayOfMonth,
                    IsEnabled = $isEnabled,
                    LastTriggeredAt = $lastTriggeredAt,
                    UpdatedAt = $updatedAt
                WHERE Id = $id";
            BindReminderParameters(updateCommand, reminder, reminder.CreatedAt == default ? now : reminder.CreatedAt.ToString("o"), now);
            updateCommand.Parameters.AddWithValue("$id", reminder.Id);
            updateCommand.ExecuteNonQuery();
            return reminder.Id;
        }

        /// <summary>
        /// 删除提醒
        /// </summary>
        public void DeleteReminder(long reminderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var logCommand = connection.CreateCommand();
            logCommand.CommandText = "DELETE FROM ReminderTriggerLogs WHERE ReminderId = $id";
            logCommand.Parameters.AddWithValue("$id", reminderId);
            logCommand.ExecuteNonQuery();

            var reminderCommand = connection.CreateCommand();
            reminderCommand.CommandText = "DELETE FROM Reminders WHERE Id = $id";
            reminderCommand.Parameters.AddWithValue("$id", reminderId);
            reminderCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// 设置启用状态
        /// </summary>
        public void SetReminderEnabled(long reminderId, bool isEnabled)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Reminders
                SET IsEnabled = $isEnabled, UpdatedAt = $updatedAt
                WHERE Id = $id";
            command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$id", reminderId);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 记录提醒触发结果
        /// </summary>
        public void RecordTrigger(long reminderId, DateTime triggeredAt, string status, bool updateLastTriggeredAt)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var insertLog = connection.CreateCommand();
            insertLog.CommandText = @"
                INSERT INTO ReminderTriggerLogs (ReminderId, TriggeredAt, Status)
                VALUES ($reminderId, $triggeredAt, $status)";
            insertLog.Parameters.AddWithValue("$reminderId", reminderId);
            insertLog.Parameters.AddWithValue("$triggeredAt", triggeredAt.ToString("o"));
            insertLog.Parameters.AddWithValue("$status", status);
            insertLog.ExecuteNonQuery();

            if (!updateLastTriggeredAt)
            {
                return;
            }

            var updateReminder = connection.CreateCommand();
            updateReminder.CommandText = @"
                UPDATE Reminders
                SET LastTriggeredAt = $lastTriggeredAt, UpdatedAt = $updatedAt
                WHERE Id = $id";
            updateReminder.Parameters.AddWithValue("$lastTriggeredAt", triggeredAt.ToString("o"));
            updateReminder.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("o"));
            updateReminder.Parameters.AddWithValue("$id", reminderId);
            updateReminder.ExecuteNonQuery();
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

        private static void BindReminderParameters(SqliteCommand command, Reminder reminder, string createdAt, string updatedAt)
        {
            command.Parameters.AddWithValue("$category", reminder.Category);
            command.Parameters.AddWithValue("$title", reminder.Title);
            command.Parameters.AddWithValue("$message", reminder.Message);
            command.Parameters.AddWithValue("$timeText", reminder.TimeText);
            command.Parameters.AddWithValue("$recurrenceType", reminder.RecurrenceType);
            command.Parameters.AddWithValue("$intervalMinutes", reminder.IntervalMinutes);
            command.Parameters.AddWithValue("$dayOfMonth", reminder.DayOfMonth);
            command.Parameters.AddWithValue("$isEnabled", reminder.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$lastTriggeredAt", reminder.LastTriggeredAt?.ToString("o") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", createdAt);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
        }

        private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"PRAGMA table_info({tableName})";

            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
            alterCommand.ExecuteNonQuery();
        }
    }
}