using System;
using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dbinspect <path-to-db>");
    return 1;
}

var dbPath = args[0];
if (!System.IO.File.Exists(dbPath))
{
    Console.WriteLine($"Database file not found: {dbPath}");
    return 2;
}

Console.WriteLine($"Inspecting DB: {dbPath}\n");

try
{
    var cs = $"Data Source={dbPath}";
    using var conn = new SqliteConnection(cs);
    conn.Open();

    // PRAGMA user_version
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "PRAGMA user_version;";
        Console.WriteLine($"PRAGMA user_version: {cmd.ExecuteScalar()}\n");
    }

    // list tables with row counts
    var tables = new System.Collections.Generic.List<string>();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));
    }

    Console.WriteLine($"{"Table",-30} {"Rows",8}");
    Console.WriteLine(new string('-', 40));
    foreach (var tbl in tables)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{tbl}]";
        var count = cmd.ExecuteScalar();
        Console.WriteLine($"{tbl,-30} {count,8}");
    }

    // AppSettings contents
    Console.WriteLine("\n--- AppSettings 内容 ---");
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT SettingKey, SettingValue FROM AppSettings ORDER BY SettingKey";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            Console.WriteLine($"  {reader.GetString(0)} = {reader.GetString(1)}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Error inspecting DB: " + ex);
    return 3;
}
