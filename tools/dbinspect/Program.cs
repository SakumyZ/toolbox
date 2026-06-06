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

    // list tables
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT name, type, sql FROM sqlite_master WHERE type IN ('table','view') ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            var sql = reader.IsDBNull(2) ? "" : reader.GetString(2);
            Console.WriteLine($"{type.ToUpper()}: {name}");
            Console.WriteLine(sql);
            Console.WriteLine(new string('-', 60));
        }
    }

    Console.WriteLine("\nColumn info for key tables:\n");
    var keyTables = new[] { "SshConfigPreset", "SshConfigPresets", "Reminder", "Reminders", "ScriptDefinition", "ScriptDefinitions", "AppSettings" };
    foreach (var tbl in keyTables)
    {
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = $"PRAGMA table_info('{tbl}');";
        using var r2 = cmd2.ExecuteReader();
        if (!r2.HasRows)
        {
            // skip
            continue;
        }
        Console.WriteLine($"Table: {tbl}");
        Console.WriteLine("cid | name | type | notnull | dflt_value | pk");
        while (r2.Read())
        {
            var cid = r2.GetInt32(0);
            var name = r2.IsDBNull(1) ? "" : r2.GetString(1);
            var type = r2.IsDBNull(2) ? "" : r2.GetString(2);
            var notnull = r2.GetInt32(3);
            var dflt = r2.IsDBNull(4) ? "" : r2.GetString(4);
            var pk = r2.GetInt32(5);
            Console.WriteLine($"{cid} | {name} | {type} | {notnull} | {dflt} | {pk}");
        }
        Console.WriteLine(new string('=', 60));
    }

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Error inspecting DB: " + ex);
    return 3;
}
