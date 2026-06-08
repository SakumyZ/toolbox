using System;
using Microsoft.Data.Sqlite;

var db = Environment.GetEnvironmentVariable("DB_PATH");
using var conn = new SqliteConnection($"Data Source={db}");
conn.Open();

var tables = new[] { "Snippets", "Folders", "Tags", "Reminders", "Scripts", "ScriptParameters",
                     "SshConfigPresets", "SkillMetadata", "AppSettings", "ServiceGroups" };

foreach (var t in tables)
{
    try {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {t}";
        var count = cmd.ExecuteScalar();
        Console.WriteLine($"{t}: {count} Ìõ");
    } catch (Exception ex) {
        Console.WriteLine($"{t}: (´íÎó) {ex.Message}");
    }
}
