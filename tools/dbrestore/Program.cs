using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

if (args.Length < 2)
{
    Console.WriteLine("Usage: dbrestore <source-backup-db> <dest-db>");
    return 1;
}

var sourcePath = args[0];
var destPath = args[1];
if (!System.IO.File.Exists(sourcePath)) { Console.WriteLine($"Source not found: {sourcePath}"); return 2; }
if (!System.IO.File.Exists(destPath)) { Console.WriteLine($"Dest not found: {destPath}"); return 3; }

Console.WriteLine($"Source: {sourcePath}\nDest: {destPath}\n");

var sourceCs = $"Data Source={sourcePath}";
var destCs = $"Data Source={destPath}";

using var src = new SqliteConnection(sourceCs);
using var dst = new SqliteConnection(destCs);
src.Open();
dst.Open();

action_copy_ssh(src, dst);
action_copy_scripts_and_params(src, dst, out var scriptIdMap);
action_copy_reminders(src, dst, scriptIdMap);

Console.WriteLine("Restore completed.");
return 0;

void action_copy_ssh(SqliteConnection s, SqliteConnection d)
{
    Console.WriteLine("Restoring SshConfigPresets...");
    using var cmd = s.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Description, Content, IsActive, LastUsedAt, CreatedAt, UpdatedAt FROM SshConfigPresets";
    using var r = cmd.ExecuteReader();
    int inserted = 0, updated = 0;
    while (r.Read())
    {
        var name = r.GetString(1);
        var desc = r.IsDBNull(2) ? string.Empty : r.GetString(2);
        var content = r.IsDBNull(3) ? string.Empty : r.GetString(3);
        var isActive = r.GetInt32(4);
        var lastUsed = r.IsDBNull(5) ? null : r.GetString(5);
        var createdAt = r.IsDBNull(6) ? DateTime.UtcNow.ToString("o") : r.GetString(6);
        var updatedAt = r.IsDBNull(7) ? DateTime.UtcNow.ToString("o") : r.GetString(7);

        using var check = d.CreateCommand();
        check.CommandText = "SELECT Id FROM SshConfigPresets WHERE Name = @name";
        check.Parameters.AddWithValue("@name", name);
        var existing = check.ExecuteScalar();
        if (existing != null && existing != DBNull.Value)
        {
            using var up = d.CreateCommand();
            up.CommandText = @"UPDATE SshConfigPresets SET Description=@desc, Content=@content, IsActive=@isActive, LastUsedAt=@lastUsedAt, UpdatedAt=@updatedAt WHERE Name=@name";
            up.Parameters.AddWithValue("@desc", desc);
            up.Parameters.AddWithValue("@content", content);
            up.Parameters.AddWithValue("@isActive", isActive);
            up.Parameters.AddWithValue("@lastUsedAt", (object?)lastUsed ?? DBNull.Value);
            up.Parameters.AddWithValue("@updatedAt", updatedAt);
            up.Parameters.AddWithValue("@name", name);
            up.ExecuteNonQuery();
            updated++;
        }
        else
        {
            using var ins = d.CreateCommand();
            ins.CommandText = @"INSERT INTO SshConfigPresets (Name, Description, Content, IsActive, LastUsedAt, CreatedAt, UpdatedAt) VALUES (@name,@desc,@content,@isActive,@lastUsedAt,@createdAt,@updatedAt)";
            ins.Parameters.AddWithValue("@name", name);
            ins.Parameters.AddWithValue("@desc", desc);
            ins.Parameters.AddWithValue("@content", content);
            ins.Parameters.AddWithValue("@isActive", isActive);
            ins.Parameters.AddWithValue("@lastUsedAt", (object?)lastUsed ?? DBNull.Value);
            ins.Parameters.AddWithValue("@createdAt", createdAt);
            ins.Parameters.AddWithValue("@updatedAt", updatedAt);
            ins.ExecuteNonQuery();
            inserted++;
        }
    }
    Console.WriteLine($"SshConfigPresets: inserted={inserted}, updated={updated}");
}

void action_copy_scripts_and_params(SqliteConnection s, SqliteConnection d, out Dictionary<long,long> idMap)
{
    Console.WriteLine("Restoring Scripts and ScriptParameters...");
    idMap = new Dictionary<long,long>();
    using var cmd = s.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Description, ScriptType, FileName, RelativeScriptPath, WorkingDirectory, IsFavorite, IsRunInTerminal, CustomInterpreterPath, CreatedAt, UpdatedAt FROM Scripts";
    using var r = cmd.ExecuteReader();
    int inserted = 0, skipped = 0;
    while (r.Read())
    {
        var oldId = r.GetInt64(0);
        var name = r.GetString(1);
        var description = r.IsDBNull(2) ? string.Empty : r.GetString(2);
        var scriptType = r.IsDBNull(3) ? "Batch" : r.GetString(3);
        var fileName = r.IsDBNull(4) ? string.Empty : r.GetString(4);
        var relPath = r.IsDBNull(5) ? string.Empty : r.GetString(5);
        var workDir = r.IsDBNull(6) ? string.Empty : r.GetString(6);
        var isFav = r.IsDBNull(7) ? 0 : r.GetInt32(7);
        var isRunInTerminal = r.IsDBNull(8) ? 0 : r.GetInt32(8);
        var customInterp = r.IsDBNull(9) ? string.Empty : r.GetString(9);
        var createdAt = r.IsDBNull(10) ? DateTime.UtcNow.ToString("o") : r.GetString(10);
        var updatedAt = r.IsDBNull(11) ? DateTime.UtcNow.ToString("o") : r.GetString(11);

        // check by name
        using var check = d.CreateCommand();
        check.CommandText = "SELECT Id FROM Scripts WHERE Name = @name";
        check.Parameters.AddWithValue("@name", name);
        var existing = check.ExecuteScalar();
        if (existing != null && existing != DBNull.Value)
        {
            idMap[oldId] = (long)existing;
            skipped++;
            continue;
        }

        using var ins = d.CreateCommand();
        ins.CommandText = @"INSERT INTO Scripts (Name, Description, ScriptType, FileName, RelativeScriptPath, WorkingDirectory, CustomInterpreterPath, IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt) VALUES (@name,@description,@scriptType,@fileName,@relPath,@workDir,@customInterp,@isFav,@isRunInTerminal,@createdAt,@updatedAt); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@name", name);
        ins.Parameters.AddWithValue("@description", description);
        ins.Parameters.AddWithValue("@scriptType", scriptType);
        ins.Parameters.AddWithValue("@fileName", fileName);
        ins.Parameters.AddWithValue("@relPath", relPath);
        ins.Parameters.AddWithValue("@workDir", workDir);
        ins.Parameters.AddWithValue("@customInterp", customInterp);
        ins.Parameters.AddWithValue("@isFav", isFav);
        ins.Parameters.AddWithValue("@isRunInTerminal", isRunInTerminal);
        ins.Parameters.AddWithValue("@createdAt", createdAt);
        ins.Parameters.AddWithValue("@updatedAt", updatedAt);
        var newId = (long)ins.ExecuteScalar();
        idMap[oldId] = newId;
        inserted++;
    }
    Console.WriteLine($"Scripts: inserted={inserted}, skippedExisting={skipped}");

    // now script parameters
    using var pCmd = s.CreateCommand();
    pCmd.CommandText = "SELECT ScriptId, Name, DisplayName, ControlType, ArgumentName, DefaultValue, Placeholder, HelpText, IsRequired, SortOrder FROM ScriptParameters";
    using var pr = pCmd.ExecuteReader();
    int pInserted = 0;
    while (pr.Read())
    {
        var oldScriptId = pr.GetInt64(0);
        if (!idMap.ContainsKey(oldScriptId)) continue; // script not migrated
        var newScriptId = idMap[oldScriptId];
        var name = pr.GetString(1);
        var displayName = pr.IsDBNull(2) ? string.Empty : pr.GetString(2);
        var controlType = pr.IsDBNull(3) ? "Text" : pr.GetString(3);
        var argName = pr.IsDBNull(4) ? string.Empty : pr.GetString(4);
        var defVal = pr.IsDBNull(5) ? string.Empty : pr.GetString(5);
        var placeholder = pr.IsDBNull(6) ? string.Empty : pr.GetString(6);
        var helpText = pr.IsDBNull(7) ? string.Empty : pr.GetString(7);
        var isRequired = pr.IsDBNull(8) ? 0 : pr.GetInt32(8);
        var sortOrder = pr.IsDBNull(9) ? 0 : pr.GetInt32(9);

        using var ins = d.CreateCommand();
        ins.CommandText = @"INSERT INTO ScriptParameters (ScriptId, Name, DisplayName, ControlType, ArgumentName, DefaultValue, Placeholder, HelpText, IsRequired, SortOrder) VALUES (@scriptId,@name,@displayName,@controlType,@argName,@defVal,@placeholder,@helpText,@isRequired,@sortOrder)";
        ins.Parameters.AddWithValue("@scriptId", newScriptId);
        ins.Parameters.AddWithValue("@name", name);
        ins.Parameters.AddWithValue("@displayName", displayName);
        ins.Parameters.AddWithValue("@controlType", controlType);
        ins.Parameters.AddWithValue("@argName", argName);
        ins.Parameters.AddWithValue("@defVal", defVal);
        ins.Parameters.AddWithValue("@placeholder", placeholder);
        ins.Parameters.AddWithValue("@helpText", helpText);
        ins.Parameters.AddWithValue("@isRequired", isRequired);
        ins.Parameters.AddWithValue("@sortOrder", sortOrder);
        ins.ExecuteNonQuery();
        pInserted++;
    }
    Console.WriteLine($"ScriptParameters: inserted={pInserted}");
}

void action_copy_reminders(SqliteConnection s, SqliteConnection d, Dictionary<long,long> scriptIdMap)
{
    Console.WriteLine("Restoring Reminders...");
    using var cmd = s.CreateCommand();
    cmd.CommandText = "SELECT Id, Category, Title, Message, TimeText, RecurrenceType, IntervalMinutes, DayOfMonth, IsEnabled, LastTriggeredAt, CreatedAt, UpdatedAt, ActionType, ScriptId, ScriptParameters, DateText FROM Reminders";
    using var r = cmd.ExecuteReader();
    int inserted = 0, skipped = 0;
    while (r.Read())
    {
        var oldId = r.GetInt64(0);
        var category = r.IsDBNull(1) ? "自定义" : r.GetString(1);
        var title = r.GetString(2);
        var message = r.IsDBNull(3) ? string.Empty : r.GetString(3);
        var timeText = r.IsDBNull(4) ? string.Empty : r.GetString(4);
        var recurrence = r.IsDBNull(5) ? "单次" : r.GetString(5);
        var interval = r.IsDBNull(6) ? 0 : r.GetInt32(6);
        var dayOfMonth = r.IsDBNull(7) ? 1 : r.GetInt32(7);
        var isEnabled = r.IsDBNull(8) ? 1 : r.GetInt32(8);
        var lastTriggered = r.IsDBNull(9) ? null : r.GetString(9);
        var createdAt = r.IsDBNull(10) ? DateTime.UtcNow.ToString("o") : r.GetString(10);
        var updatedAt = r.IsDBNull(11) ? DateTime.UtcNow.ToString("o") : r.GetString(11);
        var actionType = r.IsDBNull(12) ? "通知提醒" : r.GetString(12);
        var scriptId = r.IsDBNull(13) ? (long?)null : r.GetInt64(13);
        var scriptParams = r.IsDBNull(14) ? string.Empty : r.GetString(14);
        var dateText = r.IsDBNull(15) ? string.Empty : r.GetString(15);

        // Avoid duplicates: check Title + TimeText
        using var chk = d.CreateCommand();
        chk.CommandText = "SELECT Id FROM Reminders WHERE Title=@title AND TimeText=@timeText";
        chk.Parameters.AddWithValue("@title", title);
        chk.Parameters.AddWithValue("@timeText", timeText);
        var existing = chk.ExecuteScalar();
        if (existing != null && existing != DBNull.Value)
        {
            skipped++;
            continue;
        }

        long? newScriptId = null;
        if (scriptId.HasValue && scriptIdMap.ContainsKey(scriptId.Value)) newScriptId = scriptIdMap[scriptId.Value];

        using var ins = d.CreateCommand();
        ins.CommandText = @"INSERT INTO Reminders (Category, Title, Message, TimeText, RecurrenceType, IntervalMinutes, DayOfMonth, IsEnabled, LastTriggeredAt, CreatedAt, UpdatedAt, ActionType, ScriptId, ScriptParameters, DateText) VALUES (@category,@title,@message,@timeText,@recurrence,@interval,@dayOfMonth,@isEnabled,@lastTriggered,@createdAt,@updatedAt,@actionType,@scriptId,@scriptParams,@dateText)";
        ins.Parameters.AddWithValue("@category", category);
        ins.Parameters.AddWithValue("@title", title);
        ins.Parameters.AddWithValue("@message", message);
        ins.Parameters.AddWithValue("@timeText", timeText);
        ins.Parameters.AddWithValue("@recurrence", recurrence);
        ins.Parameters.AddWithValue("@interval", interval);
        ins.Parameters.AddWithValue("@dayOfMonth", dayOfMonth);
        ins.Parameters.AddWithValue("@isEnabled", isEnabled);
        ins.Parameters.AddWithValue("@lastTriggered", (object?)lastTriggered ?? DBNull.Value);
        ins.Parameters.AddWithValue("@createdAt", createdAt);
        ins.Parameters.AddWithValue("@updatedAt", updatedAt);
        ins.Parameters.AddWithValue("@actionType", actionType);
        ins.Parameters.AddWithValue("@scriptId", (object?)newScriptId ?? DBNull.Value);
        ins.Parameters.AddWithValue("@scriptParams", scriptParams);
        ins.Parameters.AddWithValue("@dateText", dateText);
        ins.ExecuteNonQuery();
        inserted++;
    }
    Console.WriteLine($"Reminders: inserted={inserted}, skippedExisting={skipped}");
}
