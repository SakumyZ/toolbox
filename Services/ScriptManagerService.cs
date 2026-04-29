using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// 脚本管理与执行服务。
    /// </summary>
    public class ScriptManagerService
    {
        private readonly string _connectionString;
        private readonly string _appDataPath;
        private readonly string _scriptRootPath;

        public ScriptManagerService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolBox");
            Directory.CreateDirectory(_appDataPath);

            _scriptRootPath = Path.Combine(_appDataPath, "Scripts");
            Directory.CreateDirectory(_scriptRootPath);

            var dbPath = Path.Combine(_appDataPath, "snippets.db");
            _connectionString = $"Data Source={dbPath}";

            InitializeTables();
        }

        /// <summary>
        /// 获取全部脚本。
        /// </summary>
        public List<ScriptDefinition> GetAllScripts()
        {
            var scripts = new List<ScriptDefinition>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, Description, ScriptType, FileName, RelativeScriptPath,
                       WorkingDirectory, IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt
                FROM Scripts
                ORDER BY IsFavorite DESC, Name ASC";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                scripts.Add(new ScriptDefinition
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    ScriptType = reader.GetString(3),
                    FileName = reader.GetString(4),
                    RelativeScriptPath = reader.GetString(5),
                    WorkingDirectory = reader.GetString(6),
                    IsFavorite = reader.GetInt32(7) == 1,
                    IsRunInTerminal = reader.GetInt32(8) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
                    UpdatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind)
                });
            }

            foreach (var script in scripts)
            {
                script.Parameters = GetParametersByScriptId(connection, script.Id);
            }

            return scripts;
        }

        /// <summary>
        /// 获取单个脚本。
        /// </summary>
        public ScriptDefinition? GetScript(long scriptId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Name, Description, ScriptType, FileName, RelativeScriptPath,
                       WorkingDirectory, IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt
                FROM Scripts
                WHERE Id = $id";
            command.Parameters.AddWithValue("$id", scriptId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var script = new ScriptDefinition
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                ScriptType = reader.GetString(3),
                FileName = reader.GetString(4),
                RelativeScriptPath = reader.GetString(5),
                WorkingDirectory = reader.GetString(6),
                IsFavorite = reader.GetInt32(7) == 1,
                IsRunInTerminal = reader.GetInt32(8) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                Parameters = GetParametersByScriptId(connection, scriptId)
            };

            return script;
        }

        /// <summary>
        /// 新增或更新脚本定义，并在需要时导入脚本文件。
        /// </summary>
        public (bool success, string message, long scriptId) SaveScript(ScriptDefinition script, string? importSourcePath)
        {
            var isNewScript = script.Id <= 0;

            if (string.IsNullOrWhiteSpace(script.Name))
            {
                return (false, "脚本名称不能为空", script.Id);
            }

            if (script.Id <= 0 && string.IsNullOrWhiteSpace(importSourcePath))
            {
                return (false, "请先选择要导入的脚本文件", script.Id);
            }

            if (!string.IsNullOrWhiteSpace(importSourcePath) && !File.Exists(importSourcePath))
            {
                return (false, "选中的脚本文件不存在", script.Id);
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");
            using var transaction = connection.BeginTransaction();

            try
            {
                if (script.Id <= 0)
                {
                    var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = @"
                        INSERT INTO Scripts (Name, Description, ScriptType, FileName, RelativeScriptPath, WorkingDirectory,
                                             IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt)
                        VALUES ($name, $description, $scriptType, $fileName, $relativeScriptPath, $workingDirectory,
                                $isFavorite, $isRunInTerminal, $createdAt, $updatedAt);
                        SELECT last_insert_rowid();";
                    BindScriptParameters(insertCommand, script, now, now);
                    insertCommand.Parameters.AddWithValue("$scriptType", script.ScriptType);
                    insertCommand.Parameters.AddWithValue("$fileName", script.FileName);
                    insertCommand.Parameters.AddWithValue("$relativeScriptPath", script.RelativeScriptPath);
                    script.Id = (long)insertCommand.ExecuteScalar()!;
                }

                if (!string.IsNullOrWhiteSpace(importSourcePath))
                {
                    var importResult = ImportScriptFile(script.Id, importSourcePath!);
                    if (!importResult.success)
                    {
                        transaction.Rollback();
                        return (false, importResult.message, script.Id);
                    }

                    script.FileName = importResult.fileName;
                    script.RelativeScriptPath = importResult.relativePath;
                    script.ScriptType = importResult.scriptType;
                }

                var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = @"
                    UPDATE Scripts SET
                        Name = $name,
                        Description = $description,
                        ScriptType = $scriptType,
                        FileName = $fileName,
                        RelativeScriptPath = $relativeScriptPath,
                        WorkingDirectory = $workingDirectory,
                        IsFavorite = $isFavorite,
                        IsRunInTerminal = $isRunInTerminal,
                        UpdatedAt = $updatedAt
                    WHERE Id = $id";
                BindScriptParameters(updateCommand, script, script.CreatedAt == default ? now : script.CreatedAt.ToString("o"), now);
                updateCommand.Parameters.AddWithValue("$scriptType", script.ScriptType);
                updateCommand.Parameters.AddWithValue("$fileName", script.FileName);
                updateCommand.Parameters.AddWithValue("$relativeScriptPath", script.RelativeScriptPath);
                updateCommand.Parameters.AddWithValue("$id", script.Id);
                updateCommand.ExecuteNonQuery();

                ReplaceScriptParameters(connection, transaction, script.Id, script.Parameters);
                transaction.Commit();
                return (true, isNewScript ? "脚本已创建" : "脚本已保存", script.Id);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, $"保存失败：{ex.Message}", script.Id);
            }
        }

        /// <summary>
        /// 复制脚本定义、参数和脚本文件。
        /// </summary>
        public (bool success, string message, long scriptId) DuplicateScript(long scriptId)
        {
            var source = GetScript(scriptId);
            if (source == null)
            {
                return (false, "未找到要复制的脚本", 0);
            }

            var sourcePath = GetScriptAbsolutePath(source);
            if (!File.Exists(sourcePath))
            {
                return (false, "源脚本文件不存在，无法复制", 0);
            }

            var duplicated = new ScriptDefinition
            {
                Name = BuildDuplicateName(source.Name),
                Description = source.Description,
                ScriptType = source.ScriptType,
                WorkingDirectory = source.WorkingDirectory,
                IsFavorite = false,
                IsRunInTerminal = source.IsRunInTerminal,
                Parameters = source.Parameters.Select(parameter => new ScriptParameterDefinition
                {
                    Name = parameter.Name,
                    DisplayName = parameter.DisplayName,
                    ControlType = parameter.ControlType,
                    ArgumentName = parameter.ArgumentName,
                    DefaultValue = parameter.DefaultValue,
                    Placeholder = parameter.Placeholder,
                    HelpText = parameter.HelpText,
                    IsRequired = parameter.IsRequired,
                    SortOrder = parameter.SortOrder
                }).ToList()
            };

            return SaveScript(duplicated, sourcePath);
        }

        /// <summary>
        /// 删除脚本。
        /// </summary>
        public void DeleteScript(long scriptId)
        {
            var script = GetScript(scriptId);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var deleteParameters = connection.CreateCommand();
            deleteParameters.CommandText = "DELETE FROM ScriptParameters WHERE ScriptId = $scriptId";
            deleteParameters.Parameters.AddWithValue("$scriptId", scriptId);
            deleteParameters.ExecuteNonQuery();

            var deleteScript = connection.CreateCommand();
            deleteScript.CommandText = "DELETE FROM Scripts WHERE Id = $scriptId";
            deleteScript.Parameters.AddWithValue("$scriptId", scriptId);
            deleteScript.ExecuteNonQuery();

            if (script == null)
            {
                return;
            }

            var scriptDirectory = Path.Combine(_scriptRootPath, scriptId.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(scriptDirectory))
            {
                Directory.Delete(scriptDirectory, recursive: true);
            }
        }

        /// <summary>
        /// 执行脚本。
        /// </summary>
        public async Task<ScriptExecutionResult> ExecuteScriptAsync(ScriptDefinition script, Dictionary<long, string> parameterValues)
        {
            return await ExecuteScriptAsync(script, parameterValues, null, null);
        }

        /// <summary>
        /// 执行脚本，并在执行过程中回传输出行。
        /// </summary>
        public async Task<ScriptExecutionResult> ExecuteScriptAsync(
            ScriptDefinition script,
            Dictionary<long, string> parameterValues,
            Action<string>? onOutputLine,
            Action<string>? onErrorLine)
        {
            var result = new ScriptExecutionResult
            {
                StartedAt = DateTime.Now
            };

            var scriptPath = GetScriptAbsolutePath(script);
            if (!File.Exists(scriptPath))
            {
                result.Success = false;
                result.Message = $"脚本文件不存在：{scriptPath}";
                result.FinishedAt = DateTime.Now;
                return result;
            }

            var validationMessage = ValidateParameters(script.Parameters, parameterValues);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                result.Success = false;
                result.Message = validationMessage;
                result.FinishedAt = DateTime.Now;
                return result;
            }

            var workingDirectory = ResolveWorkingDirectory(script, scriptPath);
            var startInfo = BuildStartInfo(script, scriptPath, workingDirectory, parameterValues);
            if (startInfo == null)
            {
                result.Success = false;
                result.Message = "无法解析脚本运行器，请确认系统已安装对应环境。";
                result.FinishedAt = DateTime.Now;
                return result;
            }

            using var process = new Process
            {
                StartInfo = startInfo
            };

            try
            {
                if (onOutputLine != null || onErrorLine != null)
                {
                    ApplyStreamEncodings(startInfo, script.ScriptType);

                    var standardOutputLines = new List<string>();
                    var standardErrorLines = new List<string>();

                    process.OutputDataReceived += (_, args) =>
                    {
                        if (args.Data == null)
                        {
                            return;
                        }

                        lock (standardOutputLines)
                        {
                            standardOutputLines.Add(args.Data);
                        }

                        onOutputLine?.Invoke(args.Data);
                    };

                    process.ErrorDataReceived += (_, args) =>
                    {
                        if (args.Data == null)
                        {
                            return;
                        }

                        lock (standardErrorLines)
                        {
                            standardErrorLines.Add(args.Data);
                        }

                        onErrorLine?.Invoke(args.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    process.WaitForExit();

                    result.StandardOutput = string.Join(Environment.NewLine, standardOutputLines);
                    result.StandardError = string.Join(Environment.NewLine, standardErrorLines);
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                    result.Message = result.Success
                        ? $"执行完成，退出码 {result.ExitCode}"
                        : $"执行失败，退出码 {result.ExitCode}";
                }
                else
                {
                    process.Start();
                    var standardOutputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
                    var standardErrorTask = ReadAllBytesAsync(process.StandardError.BaseStream);
                    await process.WaitForExitAsync();

                    result.StandardOutput = DecodeProcessOutput(await standardOutputTask);
                    result.StandardError = DecodeProcessOutput(await standardErrorTask);
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                    result.Message = result.Success
                        ? $"执行完成，退出码 {result.ExitCode}"
                        : $"执行失败，退出码 {result.ExitCode}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"执行异常：{ex.Message}";
            }

            result.FinishedAt = DateTime.Now;
            return result;
        }

        /// <summary>
        /// 获取脚本绝对路径。
        /// </summary>
        public string GetScriptAbsolutePath(ScriptDefinition script)
        {
            return string.IsNullOrWhiteSpace(script.RelativeScriptPath)
                ? string.Empty
                : Path.Combine(_appDataPath, script.RelativeScriptPath);
        }

        /// <summary>
        /// 获取脚本所在目录。
        /// </summary>
        public string GetScriptDirectory(ScriptDefinition script)
        {
            var scriptPath = GetScriptAbsolutePath(script);
            return string.IsNullOrWhiteSpace(scriptPath)
                ? string.Empty
                : Path.GetDirectoryName(scriptPath) ?? string.Empty;
        }

        /// <summary>
        /// 检查脚本扩展名是否受支持。
        /// </summary>
        public bool IsSupportedScriptFile(string filePath)
        {
            return TryResolveScriptType(filePath, out _);
        }

        /// <summary>
        /// 生成可复制的命令预览文本。
        /// </summary>
        public string BuildCommandPreview(ScriptDefinition script, string scriptPath, Dictionary<long, string> parameterValues)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return string.Empty;
            }

            var tokens = BuildArgumentTokens(script.Parameters, parameterValues);
            var parts = new List<string>
            {
                QuoteScriptInvocation(script.ScriptType, scriptPath)
            };

            foreach (var token in tokens)
            {
                parts.Add(QuoteCommandPart(token));
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 在新终端中启动脚本，适用于需要交互输入的场景。
        /// </summary>
        public (bool success, string message) StartScriptInTerminal(ScriptDefinition script, Dictionary<long, string> parameterValues)
        {
            var scriptPath = GetScriptAbsolutePath(script);
            if (!File.Exists(scriptPath))
            {
                return (false, $"脚本文件不存在：{scriptPath}");
            }

            var workingDirectory = ResolveWorkingDirectory(script, scriptPath);
            var startInfo = BuildTerminalStartInfo(script, scriptPath, workingDirectory, parameterValues);
            if (startInfo == null)
            {
                return (false, "无法为当前脚本创建终端进程");
            }

            try
            {
                Process.Start(startInfo);
                return (true, "已在新终端中启动脚本");
            }
            catch (Exception ex)
            {
                return (false, $"启动终端失败：{ex.Message}");
            }
        }

        private void InitializeTables()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Scripts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    ScriptType TEXT NOT NULL DEFAULT 'Batch',
                    FileName TEXT NOT NULL DEFAULT '',
                    RelativeScriptPath TEXT NOT NULL DEFAULT '',
                    WorkingDirectory TEXT NOT NULL DEFAULT '',
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    IsRunInTerminal INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ScriptParameters (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ScriptId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    ControlType TEXT NOT NULL DEFAULT 'Text',
                    ArgumentName TEXT NOT NULL DEFAULT '',
                    DefaultValue TEXT NOT NULL DEFAULT '',
                    Placeholder TEXT NOT NULL DEFAULT '',
                    HelpText TEXT NOT NULL DEFAULT '',
                    IsRequired INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (ScriptId) REFERENCES Scripts(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Scripts_Name ON Scripts(Name);
                CREATE INDEX IF NOT EXISTS IX_ScriptParameters_ScriptId ON ScriptParameters(ScriptId);
            ";
            command.ExecuteNonQuery();
            EnsureColumnExists(connection, "Scripts", "IsRunInTerminal", "INTEGER NOT NULL DEFAULT 0");
        }

        private static void BindScriptParameters(SqliteCommand command, ScriptDefinition script, string createdAt, string updatedAt)
        {
            command.Parameters.AddWithValue("$name", script.Name);
            command.Parameters.AddWithValue("$description", script.Description);
            command.Parameters.AddWithValue("$workingDirectory", script.WorkingDirectory ?? string.Empty);
            command.Parameters.AddWithValue("$isFavorite", script.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("$isRunInTerminal", script.IsRunInTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", createdAt);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
        }

        private static List<ScriptParameterDefinition> GetParametersByScriptId(SqliteConnection connection, long scriptId)
        {
            var parameters = new List<ScriptParameterDefinition>();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, ScriptId, Name, DisplayName, ControlType, ArgumentName, DefaultValue,
                       Placeholder, HelpText, IsRequired, SortOrder
                FROM ScriptParameters
                WHERE ScriptId = $scriptId
                ORDER BY SortOrder ASC, Id ASC";
            command.Parameters.AddWithValue("$scriptId", scriptId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                parameters.Add(new ScriptParameterDefinition
                {
                    Id = reader.GetInt64(0),
                    ScriptId = reader.GetInt64(1),
                    Name = reader.GetString(2),
                    DisplayName = reader.GetString(3),
                    ControlType = reader.GetString(4),
                    ArgumentName = reader.GetString(5),
                    DefaultValue = reader.GetString(6),
                    Placeholder = reader.GetString(7),
                    HelpText = reader.GetString(8),
                    IsRequired = reader.GetInt32(9) == 1,
                    SortOrder = reader.GetInt32(10)
                });
            }

            return parameters;
        }

        private static void ReplaceScriptParameters(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long scriptId,
            List<ScriptParameterDefinition> parameters)
        {
            var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ScriptParameters WHERE ScriptId = $scriptId";
            deleteCommand.Parameters.AddWithValue("$scriptId", scriptId);
            deleteCommand.ExecuteNonQuery();

            for (int index = 0; index < parameters.Count; index++)
            {
                var parameter = parameters[index];
                var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = @"
                    INSERT INTO ScriptParameters (
                        ScriptId, Name, DisplayName, ControlType, ArgumentName, DefaultValue,
                        Placeholder, HelpText, IsRequired, SortOrder)
                    VALUES (
                        $scriptId, $name, $displayName, $controlType, $argumentName, $defaultValue,
                        $placeholder, $helpText, $isRequired, $sortOrder)";
                insertCommand.Parameters.AddWithValue("$scriptId", scriptId);
                insertCommand.Parameters.AddWithValue("$name", parameter.Name);
                insertCommand.Parameters.AddWithValue("$displayName", parameter.DisplayName);
                insertCommand.Parameters.AddWithValue("$controlType", parameter.ControlType);
                insertCommand.Parameters.AddWithValue("$argumentName", parameter.ArgumentName);
                insertCommand.Parameters.AddWithValue("$defaultValue", parameter.DefaultValue);
                insertCommand.Parameters.AddWithValue("$placeholder", parameter.Placeholder);
                insertCommand.Parameters.AddWithValue("$helpText", parameter.HelpText);
                insertCommand.Parameters.AddWithValue("$isRequired", parameter.IsRequired ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$sortOrder", parameter.SortOrder > 0 ? parameter.SortOrder : index + 1);
                insertCommand.ExecuteNonQuery();
            }
        }

        private (bool success, string message, string fileName, string relativePath, string scriptType) ImportScriptFile(long scriptId, string sourcePath)
        {
            if (!TryResolveScriptType(sourcePath, out var scriptType))
            {
                return (false, "仅支持导入 .bat、.ps1、.sh 脚本", string.Empty, string.Empty, string.Empty);
            }

            var scriptDirectory = Path.Combine(_scriptRootPath, scriptId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(scriptDirectory);

            var fileName = Path.GetFileName(sourcePath);
            var destinationPath = Path.Combine(scriptDirectory, fileName);
            File.Copy(sourcePath, destinationPath, overwrite: true);

            var relativePath = Path.Combine("Scripts", scriptId.ToString(CultureInfo.InvariantCulture), fileName);
            return (true, "脚本文件已导入", fileName, relativePath, scriptType);
        }

        private static bool TryResolveScriptType(string filePath, out string scriptType)
        {
            scriptType = ScriptTypes.Batch;
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            switch (extension)
            {
                case ".bat":
                case ".cmd":
                    scriptType = ScriptTypes.Batch;
                    return true;
                case ".ps1":
                    scriptType = ScriptTypes.PowerShell;
                    return true;
                case ".sh":
                    scriptType = ScriptTypes.Shell;
                    return true;
                default:
                    return false;
            }
        }

        private static string ValidateParameters(List<ScriptParameterDefinition> parameters, Dictionary<long, string> parameterValues)
        {
            foreach (var parameter in parameters)
            {
                parameterValues.TryGetValue(parameter.Id, out var value);

                if (IsBooleanControlType(parameter.ControlType))
                {
                    if (parameter.IsRequired && !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"参数“{ResolveDisplayName(parameter)}”为必选项";
                    }

                    continue;
                }

                if (parameter.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    return $"参数“{ResolveDisplayName(parameter)}”不能为空";
                }
            }

            return string.Empty;
        }

        private static string ResolveWorkingDirectory(ScriptDefinition script, string scriptPath)
        {
            if (!string.IsNullOrWhiteSpace(script.WorkingDirectory))
            {
                return script.WorkingDirectory;
            }

            return Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory;
        }

        private static ProcessStartInfo? BuildStartInfo(
            ScriptDefinition script,
            string scriptPath,
            string workingDirectory,
            Dictionary<long, string> parameterValues)
        {
            var tokens = BuildArgumentTokens(script.Parameters, parameterValues);
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            switch (script.ScriptType)
            {
                case ScriptTypes.Batch:
                    startInfo.FileName = "cmd.exe";
                    startInfo.ArgumentList.Add("/c");
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                case ScriptTypes.PowerShell:
                    startInfo.FileName = ResolvePowerShellExecutable();
                    if (string.IsNullOrWhiteSpace(startInfo.FileName))
                    {
                        return null;
                    }

                    startInfo.ArgumentList.Add("-ExecutionPolicy");
                    startInfo.ArgumentList.Add("Bypass");
                    startInfo.ArgumentList.Add("-File");
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                case ScriptTypes.Shell:
                    startInfo.FileName = ResolveShellExecutable();
                    if (string.IsNullOrWhiteSpace(startInfo.FileName))
                    {
                        return null;
                    }

                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                default:
                    return null;
            }

            foreach (var token in tokens)
            {
                startInfo.ArgumentList.Add(token);
            }

            return startInfo;
        }

        private static ProcessStartInfo? BuildTerminalStartInfo(
            ScriptDefinition script,
            string scriptPath,
            string workingDirectory,
            Dictionary<long, string> parameterValues)
        {
            var tokens = BuildArgumentTokens(script.Parameters, parameterValues);

            switch (script.ScriptType)
            {
                case ScriptTypes.Batch:
                {
                    var command = BuildCommandLine(QuoteCommandPart(scriptPath), tokens);
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.PowerShell:
                {
                    var executable = ResolvePowerShellExecutable();
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                    startInfo.ArgumentList.Add("-NoExit");
                    startInfo.ArgumentList.Add("-ExecutionPolicy");
                    startInfo.ArgumentList.Add("Bypass");
                    startInfo.ArgumentList.Add("-File");
                    startInfo.ArgumentList.Add(scriptPath);
                    foreach (var token in tokens)
                    {
                        startInfo.ArgumentList.Add(token);
                    }

                    return startInfo;
                }
                case ScriptTypes.Shell:
                {
                    var executable = ResolveShellExecutable();
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                    startInfo.ArgumentList.Add(scriptPath);
                    foreach (var token in tokens)
                    {
                        startInfo.ArgumentList.Add(token);
                    }

                    return startInfo;
                }
                default:
                    return null;
            }
        }

        private static List<string> BuildArgumentTokens(
            List<ScriptParameterDefinition> parameters,
            Dictionary<long, string> parameterValues)
        {
            var tokens = new List<string>();

            foreach (var parameter in parameters.OrderBy(item => item.SortOrder).ThenBy(item => item.Id))
            {
                parameterValues.TryGetValue(parameter.Id, out var value);
                value ??= string.Empty;

                if (IsBooleanControlType(parameter.ControlType))
                {
                    var normalizedValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                        ? "true"
                        : "false";

                    if (!string.IsNullOrWhiteSpace(parameter.ArgumentName))
                    {
                        tokens.Add(parameter.ArgumentName.Trim());
                        tokens.Add(normalizedValue);
                    }
                    else
                    {
                        tokens.Add(normalizedValue);
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(parameter.ArgumentName))
                {
                    tokens.Add(parameter.ArgumentName.Trim());
                }

                tokens.Add(value);
            }

            return tokens;
        }

        private static string ResolveDisplayName(ScriptParameterDefinition parameter)
        {
            if (!string.IsNullOrWhiteSpace(parameter.DisplayName))
            {
                return parameter.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(parameter.Name))
            {
                return parameter.Name;
            }

            return "未命名参数";
        }

        private static string ResolveShellExecutable()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathFolders = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var candidates = new[]
            {
                "bash.exe",
                "sh.exe"
            };

            foreach (var folder in pathFolders)
            {
                foreach (var candidate in candidates)
                {
                    var fullPath = Path.Combine(folder, candidate);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            return string.Empty;
        }

        private static string ResolvePowerShellExecutable()
        {
            var pwshPath = FindExecutableFromPath("pwsh.exe");
            if (!string.IsNullOrWhiteSpace(pwshPath))
            {
                return pwshPath;
            }

            var windowsPowerShell = FindExecutableFromPath("powershell.exe");
            if (!string.IsNullOrWhiteSpace(windowsPowerShell))
            {
                return windowsPowerShell;
            }

            return "powershell.exe";
        }

private static bool IsBooleanControlType(string? controlType)
    {
        return string.Equals(controlType, ScriptParameterControlTypes.Boolean, StringComparison.OrdinalIgnoreCase);
    }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        private static void ApplyStreamEncodings(ProcessStartInfo startInfo, string scriptType)
        {
            if (string.Equals(scriptType, ScriptTypes.PowerShell, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                startInfo.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                return;
            }

            var encoding = GetOemEncoding();
            startInfo.StandardOutputEncoding = encoding;
            startInfo.StandardErrorEncoding = encoding;
        }

        private static string DecodeProcessOutput(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            if (HasUtf8Bom(bytes))
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            if (HasUtf16LeBom(bytes))
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (HasUtf16BeBom(bytes))
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                return utf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return GetOemEncoding().GetString(bytes);
            }
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 &&
                   bytes[0] == 0xEF &&
                   bytes[1] == 0xBB &&
                   bytes[2] == 0xBF;
        }

        private static bool HasUtf16LeBom(byte[] bytes)
        {
            return bytes.Length >= 2 &&
                   bytes[0] == 0xFF &&
                   bytes[1] == 0xFE;
        }

        private static bool HasUtf16BeBom(byte[] bytes)
        {
            return bytes.Length >= 2 &&
                   bytes[0] == 0xFE &&
                   bytes[1] == 0xFF;
        }

        private static Encoding GetOemEncoding()
        {
            try
            {
                return Encoding.GetEncoding((int)GetOEMCP());
            }
            catch (ArgumentException)
            {
                return Encoding.Default;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

        private static string QuoteCommandPart(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (!value.Contains(' ') && !value.Contains('"'))
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        private static string BuildCommandLine(string firstPart, List<string> tokens)
        {
            var parts = new List<string> { firstPart };
            foreach (var token in tokens)
            {
                parts.Add(QuoteCommandPart(token));
            }

            return string.Join(" ", parts);
        }

        private static string QuoteScriptInvocation(string scriptType, string scriptPath)
        {
            var quotedScriptPath = QuoteCommandPart(scriptPath);

            if (string.Equals(scriptType, ScriptTypes.PowerShell, StringComparison.OrdinalIgnoreCase))
            {
                return $"& {quotedScriptPath}";
            }

            return quotedScriptPath;
        }

        private static string FindExecutableFromPath(string executableName)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathFolders = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var folder in pathFolders)
            {
                try
                {
                    var fullPath = Path.Combine(folder, executableName);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch (ArgumentException)
                {
                    // ignore invalid PATH segments
                }
            }

            return string.Empty;
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

        private string BuildDuplicateName(string sourceName)
        {
            var existingNames = GetAllScripts()
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var baseName = string.IsNullOrWhiteSpace(sourceName) ? "脚本副本" : $"{sourceName} - 副本";
            var candidate = baseName;
            var index = 2;

            while (existingNames.Contains(candidate))
            {
                candidate = $"{baseName} {index}";
                index++;
            }

            return candidate;
        }
    }
}
