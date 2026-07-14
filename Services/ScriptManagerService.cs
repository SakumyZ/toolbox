using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ToolBox.Models;
using Serilog;

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

            Log.Information("ScriptManagerService: 正在连接脚本库数据库: {DbPath}", dbPath);
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
                       WorkingDirectory, CustomInterpreterPath, IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt, CliCommandPath
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
                    CommandPrefix = reader.GetString(7),
                    IsFavorite = reader.GetInt32(8) == 1,
                    IsRunInTerminal = reader.GetInt32(9) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                    UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
                    CliCommandPath = reader.IsDBNull(12) ? null : reader.GetString(12)
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
                       WorkingDirectory, CustomInterpreterPath, IsFavorite, IsRunInTerminal, CreatedAt, UpdatedAt, CliCommandPath
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
                CommandPrefix = reader.GetString(7),
                IsFavorite = reader.GetInt32(8) == 1,
                IsRunInTerminal = reader.GetInt32(9) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
                CliCommandPath = reader.IsDBNull(12) ? null : reader.GetString(12),
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
                                             CustomInterpreterPath, IsFavorite, IsRunInTerminal, CliCommandPath, CreatedAt, UpdatedAt)
                        VALUES ($name, $description, $scriptType, $fileName, $relativeScriptPath, $workingDirectory,
                                $commandPrefix, $isFavorite, $isRunInTerminal, $cliCommandPath, $createdAt, $updatedAt);
                        SELECT last_insert_rowid();";
                    BindScriptParameters(insertCommand, script, now, now);
                    insertCommand.Parameters.AddWithValue("$scriptType", script.ScriptType ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("$fileName", script.FileName ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("$relativeScriptPath", script.RelativeScriptPath ?? string.Empty);
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
                        CustomInterpreterPath = $commandPrefix,
                        IsFavorite = $isFavorite,
                        IsRunInTerminal = $isRunInTerminal,
                        CliCommandPath = $cliCommandPath,
                        UpdatedAt = $updatedAt
                    WHERE Id = $id";
                BindScriptParameters(updateCommand, script, script.CreatedAt == default ? now : script.CreatedAt.ToString("o"), now);
                updateCommand.Parameters.AddWithValue("$scriptType", script.ScriptType ?? string.Empty);
                updateCommand.Parameters.AddWithValue("$fileName", script.FileName ?? string.Empty);
                updateCommand.Parameters.AddWithValue("$relativeScriptPath", script.RelativeScriptPath ?? string.Empty);
                updateCommand.Parameters.AddWithValue("$id", script.Id);
                updateCommand.ExecuteNonQuery();

                ReplaceScriptParameters(connection, transaction, script.Id, script.Parameters);
                transaction.Commit();
                Log.Information("ScriptManagerService: 脚本 '{ScriptName}' 保存成功 (ID = {ScriptId})", script.Name, script.Id);
                return (true, isNewScript ? "脚本已创建" : "脚本已保存", script.Id);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Log.Error(ex, "ScriptManagerService: 保存脚本 '{ScriptName}' 发生错误", script.Name);
                return (false, $"保存失败：{ex.Message}", script.Id);
            }
        }

        /// <summary>
        /// 复制脚本定义、参数和脚本文件。
        /// </summary>
        public (bool success, string message, long scriptId) DuplicateScript(long scriptId)
        {
            Log.Information("ScriptManagerService: 正在复制脚本 ID = {ScriptId}", scriptId);
            var source = GetScript(scriptId);
            if (source == null)
            {
                Log.Warning("ScriptManagerService: 复制脚本失败，找不到脚本 ID = {ScriptId}", scriptId);
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
                CommandPrefix = source.CommandPrefix,
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
            Log.Warning("ScriptManagerService: 正在删除脚本 ID = {ScriptId}", scriptId);
            var script = GetScript(scriptId);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var deleteParameters = connection.CreateCommand();
            deleteParameters.CommandText = "DELETE FROM ScriptParameters WHERE ScriptId = $scriptId";
            deleteParameters.Parameters.AddWithValue("$scriptId", scriptId);
            deleteParameters.ExecuteNonQuery();

            var deleteLogs = connection.CreateCommand();
            deleteLogs.CommandText = "DELETE FROM ScriptExecutionLogs WHERE ScriptId = $scriptId";
            deleteLogs.Parameters.AddWithValue("$scriptId", scriptId);
            deleteLogs.ExecuteNonQuery();

            var deleteScript = connection.CreateCommand();
            deleteScript.CommandText = "DELETE FROM Scripts WHERE Id = $scriptId";
            deleteScript.Parameters.AddWithValue("$scriptId", scriptId);
            deleteScript.ExecuteNonQuery();

            if (script == null)
            {
                Log.Warning("ScriptManagerService: 脚本 ID = {ScriptId} 数据库记录已删除，但找不到其本地文件定义", scriptId);
                return;
            }

            var scriptDirectory = Path.Combine(_scriptRootPath, scriptId.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(scriptDirectory))
            {
                try
                {
                    Directory.Delete(scriptDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ScriptManagerService: 无法物理删除脚本托管目录: {ScriptDirectory}", scriptDirectory);
                }
            }
            Log.Information("ScriptManagerService: 脚本 ID = {ScriptId} 及其本地托管文件已成功删除", scriptId);
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
            return await ExecuteScriptAsync(
                script,
                parameterValues,
                onOutputLine,
                onErrorLine,
                ScriptExecutionSources.Manual,
                null,
                null);
        }

        /// <summary>
        /// 执行脚本，并写入执行日志。
        /// </summary>
        public async Task<ScriptExecutionResult> ExecuteScriptAsync(
            ScriptDefinition script,
            Dictionary<long, string> parameterValues,
            Action<string>? onOutputLine,
            Action<string>? onErrorLine,
            string source,
            long? reminderId,
            long? existingLogId)
        {
            Log.Information("ScriptManagerService: 准备启动后台脚本执行 -> '{ScriptName}' (ID = {ScriptId}, 来源 = '{Source}')", script.Name, script.Id, source);
            var result = new ScriptExecutionResult
            {
                StartedAt = DateTime.Now
            };

            var scriptPath = GetScriptAbsolutePath(script);
            var workingDirectory = File.Exists(scriptPath)
                ? ResolveWorkingDirectory(script, scriptPath)
                : string.Empty;
            var commandPreview = File.Exists(scriptPath)
                ? BuildCommandPreview(script, scriptPath, parameterValues)
                : string.Empty;
            var logId = existingLogId ?? CreateExecutionLog(
                script,
                source,
                reminderId,
                result.StartedAt,
                commandPreview,
                workingDirectory,
                parameterValues);

            if (!File.Exists(scriptPath))
            {
                result.Success = false;
                result.Message = $"脚本文件不存在：{scriptPath}";
                result.FinishedAt = DateTime.Now;
                CompleteExecutionLog(logId, result);
                return result;
            }

            var validationMessage = ValidateParameters(script.Parameters, parameterValues);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                result.Success = false;
                result.Message = validationMessage;
                result.FinishedAt = DateTime.Now;
                CompleteExecutionLog(logId, result);
                return result;
            }

            var startInfo = BuildStartInfo(script, scriptPath, workingDirectory, parameterValues);
            if (startInfo == null)
            {
                result.Success = false;
                result.Message = "无法解析脚本运行器，请确认系统已安装对应环境。";
                result.FinishedAt = DateTime.Now;
                CompleteExecutionLog(logId, result);
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
                    // 分支注释：流式捕获时不设置 StandardOutputEncoding，依靠从 BaseStream 异步读取原始字节来自适应解码
                    var standardOutputLines = new List<string>();
                    var standardErrorLines = new List<string>();

                    process.Start();

                    // 启动异步任务对标准输出流做自适应字节行切割与解码
                    var outputTask = ReadStreamLineByLineAsync(process.StandardOutput.BaseStream, line =>
                    {
                        lock (standardOutputLines)
                        {
                            standardOutputLines.Add(line);
                        }
                        onOutputLine?.Invoke(line);
                    });

                    // 启动异步任务对错误输出流做自适应字节行切割与解码
                    var errorTask = ReadStreamLineByLineAsync(process.StandardError.BaseStream, line =>
                    {
                        lock (standardErrorLines)
                        {
                            standardErrorLines.Add(line);
                        }
                        onErrorLine?.Invoke(line);
                    });

                    // 等待进程退出且两个流异步读取任务全部执行完毕
                    await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);

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
                Log.Information("ScriptManagerService: 脚本 '{ScriptName}' 后台执行完成，退出码: {ExitCode}, 成功 = {Success}", script.Name, result.ExitCode, result.Success);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScriptManagerService: 脚本 '{ScriptName}' 执行发生异常", script.Name);
                result.Success = false;
                result.Message = $"执行异常：{ex.Message}";
            }

            result.FinishedAt = DateTime.Now;
            CompleteExecutionLog(logId, result);
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

            var workingDirectory = ResolveWorkingDirectory(script, scriptPath);
            var tokens = BuildArgumentTokens(script.Parameters, parameterValues);
            var parts = BuildCommandParts(script, scriptPath, workingDirectory, tokens);

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 构建命令行各部分。
        /// </summary>
        private static List<string> BuildCommandParts(ScriptDefinition script, string scriptPath, string workingDirectory, List<string> tokens)
        {
            var parts = new List<string>();
            var scriptType = script.ScriptType;
            if (TryParseCommandPrefix(script.CommandPrefix, out var commandPrefixTokens))
            {
                foreach (var prefixToken in commandPrefixTokens)
                {
                    parts.Add(FormatCommandToken(prefixToken));
                }

                parts.Add(QuoteCommandPart(scriptPath));

                foreach (var token in tokens)
                {
                    parts.Add(QuoteCommandPart(token));
                }

                return parts;
            }

            switch (scriptType)
            {
                case ScriptTypes.Batch:
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.PowerShell:
                    parts.Add("&");
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Shell:
                    var shellExe = ResolveShellExecutable();
                    if (!string.IsNullOrWhiteSpace(shellExe))
                    {
                        parts.Add(QuoteCommandPart(shellExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Python:
                    var pythonExe = ResolvePythonExecutable(workingDirectory);

                    if (!string.IsNullOrWhiteSpace(pythonExe))
                    {
                        if (pythonExe.EndsWith("uv.exe", StringComparison.OrdinalIgnoreCase) ||
                            pythonExe.Equals("uv", StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add(QuoteCommandPart(pythonExe));
                            parts.Add("run");
                        }
                        else
                        {
                            parts.Add(QuoteCommandPart(pythonExe));
                        }
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Node:
                    var nodeExe = ResolveNodeExecutable(workingDirectory);
                    if (!string.IsNullOrWhiteSpace(nodeExe))
                    {
                        parts.Add(QuoteCommandPart(nodeExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Ruby:
                    var rubyExe = ResolveRubyExecutable(workingDirectory);
                    if (!string.IsNullOrWhiteSpace(rubyExe))
                    {
                        parts.Add(QuoteCommandPart(rubyExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Perl:
                    var perlExe = FindExecutableFromPath("perl.exe");
                    if (!string.IsNullOrWhiteSpace(perlExe))
                    {
                        parts.Add(QuoteCommandPart(perlExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.PHP:
                    var phpExe = FindExecutableFromPath("php.exe");
                    if (!string.IsNullOrWhiteSpace(phpExe))
                    {
                        parts.Add(QuoteCommandPart(phpExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                case ScriptTypes.Lua:
                    var luaExe = FindExecutableFromPath("lua.exe");
                    if (string.IsNullOrWhiteSpace(luaExe))
                    {
                        luaExe = FindExecutableFromPath("lua53.exe");
                    }
                    if (!string.IsNullOrWhiteSpace(luaExe))
                    {
                        parts.Add(QuoteCommandPart(luaExe));
                    }
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;

                default:
                    parts.Add(QuoteCommandPart(scriptPath));
                    break;
            }

            foreach (var token in tokens)
            {
                parts.Add(QuoteCommandPart(token));
            }

            return parts;
        }

        private static bool TryParseCommandPrefix(string? commandPrefix, out List<string> tokens)
        {
            tokens = new List<string>();

            if (string.IsNullOrWhiteSpace(commandPrefix))
            {
                return false;
            }

            var currentToken = new StringBuilder();
            var inQuotes = false;

            foreach (var character in commandPrefix)
            {
                if (character == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(character) && !inQuotes)
                {
                    if (currentToken.Length > 0)
                    {
                        tokens.Add(currentToken.ToString());
                        currentToken.Clear();
                    }

                    continue;
                }

                currentToken.Append(character);
            }

            if (currentToken.Length > 0)
            {
                tokens.Add(currentToken.ToString());
            }

            return tokens.Count > 0;
        }

        private static string FormatCommandToken(string token)
        {
            return token == "&" ? token : QuoteCommandPart(token);
        }

        /// <summary>
        /// 在新终端中启动脚本，适用于需要交互输入的场景。
        /// </summary>
        public (bool success, string message) StartScriptInTerminal(ScriptDefinition script, Dictionary<long, string> parameterValues)
        {
            Log.Information("ScriptManagerService: 准备在新终端启动交互式脚本 -> '{ScriptName}' (ID = {ScriptId})", script.Name, script.Id);
            var scriptPath = GetScriptAbsolutePath(script);
            if (!File.Exists(scriptPath))
            {
                Log.Error("ScriptManagerService: 启动终端失败，脚本文件不存在: {ScriptPath}", scriptPath);
                return (false, $"脚本文件不存在：{scriptPath}");
            }

            var workingDirectory = ResolveWorkingDirectory(script, scriptPath);
            var startInfo = BuildTerminalStartInfo(script, scriptPath, workingDirectory, parameterValues);
            if (startInfo == null)
            {
                Log.Error("ScriptManagerService: 无法为脚本创建终端进程");
                return (false, "无法为当前脚本创建终端进程");
            }

            long? launchLogId = null;
            var startedAt = DateTime.Now;
            try
            {
                launchLogId = CreateExecutionLog(
                    script,
                    ScriptExecutionSources.Manual,
                    null,
                    startedAt,
                    BuildCommandPreview(script, scriptPath, parameterValues),
                    workingDirectory,
                    parameterValues);

                Process.Start(startInfo);
                Log.Information("ScriptManagerService: 成功在新终端启动进程 '{FileName}'", startInfo.FileName);
                CompleteLaunchedExecutionLog(launchLogId.Value, startedAt, "已在新终端中启动脚本");
                return (true, "已在新终端中启动脚本");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScriptManagerService: 启动终端进程发生异常");
                if (launchLogId.HasValue)
                {
                    CompleteFailedExecutionLog(launchLogId.Value, startedAt, $"启动终端失败：{ex.Message}");
                }

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
                    CustomInterpreterPath TEXT NOT NULL DEFAULT '',
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

                CREATE TABLE IF NOT EXISTS ScriptExecutionLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ScriptId INTEGER NOT NULL,
                    ReminderId INTEGER NULL,
                    Source TEXT NOT NULL DEFAULT 'Manual',
                    Status TEXT NOT NULL DEFAULT 'Running',
                    ExitCode INTEGER NULL,
                    StartedAt TEXT NOT NULL,
                    FinishedAt TEXT NULL,
                    DurationMs INTEGER NULL,
                    CommandPreview TEXT NOT NULL DEFAULT '',
                    WorkingDirectory TEXT NOT NULL DEFAULT '',
                    ParameterSnapshotJson TEXT NOT NULL DEFAULT '',
                    StandardOutput TEXT NOT NULL DEFAULT '',
                    StandardError TEXT NOT NULL DEFAULT '',
                    Message TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    ArchivedAt TEXT NULL,
                    FOREIGN KEY (ScriptId) REFERENCES Scripts(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Scripts_Name ON Scripts(Name);
                CREATE INDEX IF NOT EXISTS IX_ScriptParameters_ScriptId ON ScriptParameters(ScriptId);
                CREATE INDEX IF NOT EXISTS IX_ScriptExecutionLogs_ScriptId_StartedAt
                    ON ScriptExecutionLogs(ScriptId, StartedAt DESC);
                CREATE INDEX IF NOT EXISTS IX_ScriptExecutionLogs_ReminderId_StartedAt
                    ON ScriptExecutionLogs(ReminderId, StartedAt DESC);
            ";
            command.ExecuteNonQuery();
            EnsureColumnExists(connection, "Scripts", "IsRunInTerminal", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "Scripts", "CustomInterpreterPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumnExists(connection, "Scripts", "CliCommandPath", "TEXT NULL");
        }

        /// <summary>
        /// 创建脚本执行日志。
        /// </summary>
        public long CreateExecutionLog(
            ScriptDefinition script,
            string source,
            long? reminderId,
            DateTime startedAt,
            string commandPreview,
            string workingDirectory,
            Dictionary<long, string> parameterValues)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var now = DateTime.Now.ToString("o");
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ScriptExecutionLogs (
                    ScriptId, ReminderId, Source, Status, StartedAt, CommandPreview,
                    WorkingDirectory, ParameterSnapshotJson, CreatedAt)
                VALUES (
                    $scriptId, $reminderId, $source, $status, $startedAt, $commandPreview,
                    $workingDirectory, $parameterSnapshotJson, $createdAt);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$scriptId", script.Id);
            command.Parameters.AddWithValue("$reminderId", reminderId.HasValue ? (object)reminderId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(source) ? ScriptExecutionSources.Manual : source);
            command.Parameters.AddWithValue("$status", ScriptExecutionStatuses.Running);
            command.Parameters.AddWithValue("$startedAt", startedAt.ToString("o"));
            command.Parameters.AddWithValue("$commandPreview", commandPreview ?? string.Empty);
            command.Parameters.AddWithValue("$workingDirectory", workingDirectory ?? string.Empty);
            command.Parameters.AddWithValue("$parameterSnapshotJson", BuildParameterSnapshotJson(script, parameterValues));
            command.Parameters.AddWithValue("$createdAt", now);
            return (long)command.ExecuteScalar()!;
        }

        /// <summary>
        /// 完成脚本执行日志。
        /// </summary>
        public void CompleteExecutionLog(long logId, ScriptExecutionResult result)
        {
            var status = result.Success ? ScriptExecutionStatuses.Success : ScriptExecutionStatuses.Failed;
            var durationMs = result.FinishedAt > result.StartedAt
                ? (long)(result.FinishedAt - result.StartedAt).TotalMilliseconds
                : 0;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ScriptExecutionLogs
                SET Status = $status,
                    ExitCode = $exitCode,
                    FinishedAt = $finishedAt,
                    DurationMs = $durationMs,
                    StandardOutput = $standardOutput,
                    StandardError = $standardError,
                    Message = $message
                WHERE Id = $id";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$exitCode", result.ExitCode);
            command.Parameters.AddWithValue("$finishedAt", result.FinishedAt.ToString("o"));
            command.Parameters.AddWithValue("$durationMs", durationMs);
            command.Parameters.AddWithValue("$standardOutput", TruncateLogText(result.StandardOutput));
            command.Parameters.AddWithValue("$standardError", TruncateLogText(result.StandardError));
            command.Parameters.AddWithValue("$message", result.Message ?? string.Empty);
            command.Parameters.AddWithValue("$id", logId);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取脚本执行日志。
        /// </summary>
        public List<ScriptExecutionLog> GetExecutionLogs(long scriptId, int limit = 50)
        {
            var logs = new List<ScriptExecutionLog>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, ScriptId, ReminderId, Source, Status, ExitCode, StartedAt, FinishedAt, DurationMs,
                       CommandPreview, WorkingDirectory, ParameterSnapshotJson, StandardOutput, StandardError,
                       Message, CreatedAt, ArchivedAt
                FROM ScriptExecutionLogs
                WHERE ScriptId = $scriptId AND ArchivedAt IS NULL
                ORDER BY StartedAt DESC
                LIMIT $limit";
            command.Parameters.AddWithValue("$scriptId", scriptId);
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(ReadExecutionLog(reader));
            }

            return logs;
        }

        /// <summary>
        /// 获取单条脚本执行日志。
        /// </summary>
        public ScriptExecutionLog? GetExecutionLog(long logId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, ScriptId, ReminderId, Source, Status, ExitCode, StartedAt, FinishedAt, DurationMs,
                       CommandPreview, WorkingDirectory, ParameterSnapshotJson, StandardOutput, StandardError,
                       Message, CreatedAt, ArchivedAt
                FROM ScriptExecutionLogs
                WHERE Id = $id";
            command.Parameters.AddWithValue("$id", logId);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadExecutionLog(reader) : null;
        }

        /// <summary>
        /// 获取定时器最近的脚本执行日志。
        /// </summary>
        public ScriptExecutionLog? GetLatestExecutionLogByReminder(long reminderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, ScriptId, ReminderId, Source, Status, ExitCode, StartedAt, FinishedAt, DurationMs,
                       CommandPreview, WorkingDirectory, ParameterSnapshotJson, StandardOutput, StandardError,
                       Message, CreatedAt, ArchivedAt
                FROM ScriptExecutionLogs
                WHERE ReminderId = $reminderId AND ArchivedAt IS NULL
                ORDER BY StartedAt DESC
                LIMIT 1";
            command.Parameters.AddWithValue("$reminderId", reminderId);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadExecutionLog(reader) : null;
        }

        /// <summary>
        /// 获取定时器关联的脚本执行日志。
        /// </summary>
        public List<ScriptExecutionLog> GetExecutionLogsByReminder(long reminderId, int limit = 50)
        {
            var logs = new List<ScriptExecutionLog>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, ScriptId, ReminderId, Source, Status, ExitCode, StartedAt, FinishedAt, DurationMs,
                       CommandPreview, WorkingDirectory, ParameterSnapshotJson, StandardOutput, StandardError,
                       Message, CreatedAt, ArchivedAt
                FROM ScriptExecutionLogs
                WHERE ReminderId = $reminderId AND ArchivedAt IS NULL
                ORDER BY StartedAt DESC
                LIMIT $limit";
            command.Parameters.AddWithValue("$reminderId", reminderId);
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(ReadExecutionLog(reader));
            }

            return logs;
        }

        /// <summary>
        /// 清空指定脚本的执行日志。
        /// </summary>
        public int ClearExecutionLogs(long scriptId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ScriptExecutionLogs WHERE ScriptId = $scriptId AND Status <> $running";
            command.Parameters.AddWithValue("$scriptId", scriptId);
            command.Parameters.AddWithValue("$running", ScriptExecutionStatuses.Running);
            return command.ExecuteNonQuery();
        }

        /// <summary>
        /// 清理过期脚本执行日志。
        /// </summary>
        public int CleanupExecutionLogs(int successRetentionDays = 30, int failureRetentionDays = 90, int maxLogsPerScript = 200)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var successCutoff = DateTime.Now.AddDays(-successRetentionDays).ToString("o");
            var failureCutoff = DateTime.Now.AddDays(-failureRetentionDays).ToString("o");

            var deleteByAge = connection.CreateCommand();
            deleteByAge.CommandText = @"
                DELETE FROM ScriptExecutionLogs
                WHERE Status <> $running
                  AND (
                    (Status = $success AND StartedAt < $successCutoff)
                    OR (Status <> $success AND StartedAt < $failureCutoff)
                  )";
            deleteByAge.Parameters.AddWithValue("$running", ScriptExecutionStatuses.Running);
            deleteByAge.Parameters.AddWithValue("$success", ScriptExecutionStatuses.Success);
            deleteByAge.Parameters.AddWithValue("$successCutoff", successCutoff);
            deleteByAge.Parameters.AddWithValue("$failureCutoff", failureCutoff);
            var deleted = deleteByAge.ExecuteNonQuery();

            var trimCommand = connection.CreateCommand();
            trimCommand.CommandText = @"
                DELETE FROM ScriptExecutionLogs
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (PARTITION BY ScriptId ORDER BY StartedAt DESC) AS RowNumber
                        FROM ScriptExecutionLogs
                        WHERE Status <> $running
                    )
                    WHERE RowNumber > $maxLogsPerScript
                )";
            trimCommand.Parameters.AddWithValue("$running", ScriptExecutionStatuses.Running);
            trimCommand.Parameters.AddWithValue("$maxLogsPerScript", maxLogsPerScript);
            deleted += trimCommand.ExecuteNonQuery();

            return deleted;
        }

        private void CompleteLaunchedExecutionLog(long logId, DateTime startedAt, string message)
        {
            var result = new ScriptExecutionResult
            {
                Success = true,
                ExitCode = 0,
                StartedAt = startedAt,
                FinishedAt = DateTime.Now,
                Message = message
            };

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ScriptExecutionLogs
                SET Status = $status,
                    ExitCode = $exitCode,
                    FinishedAt = $finishedAt,
                    DurationMs = $durationMs,
                    Message = $message
                WHERE Id = $id";
            command.Parameters.AddWithValue("$status", ScriptExecutionStatuses.Launched);
            command.Parameters.AddWithValue("$exitCode", result.ExitCode);
            command.Parameters.AddWithValue("$finishedAt", result.FinishedAt.ToString("o"));
            command.Parameters.AddWithValue("$durationMs", (long)(result.FinishedAt - result.StartedAt).TotalMilliseconds);
            command.Parameters.AddWithValue("$message", result.Message);
            command.Parameters.AddWithValue("$id", logId);
            command.ExecuteNonQuery();
        }

        private void CompleteFailedExecutionLog(long logId, DateTime startedAt, string message)
        {
            CompleteExecutionLog(logId, new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = startedAt,
                FinishedAt = DateTime.Now,
                Message = message
            });
        }

        private static ScriptExecutionLog ReadExecutionLog(SqliteDataReader reader)
        {
            return new ScriptExecutionLog
            {
                Id = reader.GetInt64(0),
                ScriptId = reader.GetInt64(1),
                ReminderId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                Source = reader.GetString(3),
                Status = reader.GetString(4),
                ExitCode = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                StartedAt = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                FinishedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
                DurationMs = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                CommandPreview = reader.GetString(9),
                WorkingDirectory = reader.GetString(10),
                ParameterSnapshotJson = reader.GetString(11),
                StandardOutput = reader.GetString(12),
                StandardError = reader.GetString(13),
                Message = reader.GetString(14),
                CreatedAt = DateTime.Parse(reader.GetString(15), null, DateTimeStyles.RoundtripKind),
                ArchivedAt = reader.IsDBNull(16) ? null : DateTime.Parse(reader.GetString(16), null, DateTimeStyles.RoundtripKind)
            };
        }

        private static string BuildParameterSnapshotJson(ScriptDefinition script, Dictionary<long, string> parameterValues)
        {
            var snapshot = script.Parameters
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(parameter =>
                {
                    parameterValues.TryGetValue(parameter.Id, out var value);
                    return new
                    {
                        parameter.Id,
                        parameter.Name,
                        DisplayName = ResolveDisplayName(parameter),
                        parameter.ArgumentName,
                        Value = value ?? string.Empty
                    };
                })
                .ToList();

            return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private static string TruncateLogText(string value)
        {
            const int maxLength = 1024 * 1024;
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value[..maxLength] + Environment.NewLine + "[日志过长，已截断]";
        }

        private static void BindScriptParameters(SqliteCommand command, ScriptDefinition script, string createdAt, string updatedAt)
        {
            command.Parameters.AddWithValue("$name", script.Name ?? string.Empty);
            command.Parameters.AddWithValue("$description", script.Description ?? string.Empty);
            command.Parameters.AddWithValue("$workingDirectory", script.WorkingDirectory ?? string.Empty);
            command.Parameters.AddWithValue("$commandPrefix", script.CommandPrefix ?? string.Empty);
            command.Parameters.AddWithValue("$isFavorite", script.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("$isRunInTerminal", script.IsRunInTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$cliCommandPath", (object?)script.CliCommandPath ?? DBNull.Value);
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
                insertCommand.Parameters.AddWithValue("$name", parameter.Name ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$displayName", parameter.DisplayName ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$controlType", parameter.ControlType ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$argumentName", parameter.ArgumentName ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$defaultValue", parameter.DefaultValue ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$placeholder", parameter.Placeholder ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$helpText", parameter.HelpText ?? string.Empty);
                insertCommand.Parameters.AddWithValue("$isRequired", parameter.IsRequired ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$sortOrder", parameter.SortOrder > 0 ? parameter.SortOrder : index + 1);
                insertCommand.ExecuteNonQuery();
            }
        }

        private (bool success, string message, string fileName, string relativePath, string scriptType) ImportScriptFile(long scriptId, string sourcePath)
        {
            if (!TryResolveScriptType(sourcePath, out var scriptType))
            {
                return (false, "仅支持导入 .bat、.ps1、.sh、.py、.js、.rb、.pl、.php、.lua 等脚本文件", string.Empty, string.Empty, string.Empty);
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
                case ".py":
                    scriptType = ScriptTypes.Python;
                    return true;
                case ".js":
                case ".mjs":
                case ".cjs":
                    scriptType = ScriptTypes.Node;
                    return true;
                case ".rb":
                    scriptType = ScriptTypes.Ruby;
                    return true;
                case ".pl":
                    scriptType = ScriptTypes.Perl;
                    return true;
                case ".php":
                    scriptType = ScriptTypes.PHP;
                    return true;
                case ".lua":
                    scriptType = ScriptTypes.Lua;
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

            if (TryApplyCommandPrefix(startInfo, script.CommandPrefix, scriptPath, tokens))
            {
                return startInfo;
            }

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
                {
                    var executable = ResolveShellExecutable();

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
                case ScriptTypes.Python:
                {
                    var executable = ResolvePythonExecutable(workingDirectory);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    // 如果是 uv，使用 "uv run" 命令
                    if (executable.EndsWith("uv.exe", StringComparison.OrdinalIgnoreCase) ||
                        executable.Equals("uv", StringComparison.OrdinalIgnoreCase))
                    {
                        startInfo.FileName = executable;
                        startInfo.ArgumentList.Add("run");
                        startInfo.ArgumentList.Add(scriptPath);
                    }
                    else
                    {
                        startInfo.FileName = executable;
                        startInfo.ArgumentList.Add(scriptPath);
                    }
                    break;
                }
                case ScriptTypes.Node:
                {
                    var executable = ResolveNodeExecutable(workingDirectory);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
                case ScriptTypes.Ruby:
                {
                    var executable = ResolveRubyExecutable(workingDirectory);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
                case ScriptTypes.Perl:
                {
                    var executable = FindExecutableFromPath("perl.exe");

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
                case ScriptTypes.PHP:
                {
                    var executable = FindExecutableFromPath("php.exe");

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
                case ScriptTypes.Lua:
                {
                    var executable = FindExecutableFromPath("lua.exe");
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        executable = FindExecutableFromPath("lua53.exe");
                    }
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    startInfo.FileName = executable;
                    startInfo.ArgumentList.Add(scriptPath);
                    break;
                }
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

            if (TryBuildTerminalStartInfo(script.CommandPrefix, scriptPath, workingDirectory, tokens, out var prefixedStartInfo))
            {
                return prefixedStartInfo;
            }

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
                case ScriptTypes.Python:
                {
                    var executable = ResolvePythonExecutable(workingDirectory);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    string command;
                    // 如果是 uv，使用 "uv run" 命令
                    if (executable.EndsWith("uv.exe", StringComparison.OrdinalIgnoreCase) ||
                        executable.Equals("uv", StringComparison.OrdinalIgnoreCase))
                    {
                        command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { "run", scriptPath }.Concat(tokens).ToList());
                    }
                    else
                    {
                        command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    }

                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.Node:
                {
                    var executable = ResolveNodeExecutable(workingDirectory);
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.Ruby:
                {
                    var executable = ResolveRubyExecutable(workingDirectory);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.Perl:
                {
                    var executable = FindExecutableFromPath("perl.exe");

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.PHP:
                {
                    var executable = FindExecutableFromPath("php.exe");

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
                }
                case ScriptTypes.Lua:
                {
                    var executable = FindExecutableFromPath("lua.exe");
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        executable = FindExecutableFromPath("lua53.exe");
                    }
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        return null;
                    }

                    var command = BuildCommandLine(QuoteCommandPart(executable), new List<string> { scriptPath }.Concat(tokens).ToList());
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {command}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    };
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
            // 分支注释：在 Windows 环境下，控制台子进程重定向标准输出时，其字节流编码默认与系统 OEM 编码一致 (GBK/ANSI)
            // 统一采用 GetOemEncoding() 自适应系统代码页以实现万能兼容，消除乱码
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

        private static bool TryApplyCommandPrefix(
            ProcessStartInfo startInfo,
            string? commandPrefix,
            string scriptPath,
            List<string> tokens)
        {
            if (!TryParseCommandPrefix(commandPrefix, out var prefixTokens))
            {
                return false;
            }

            startInfo.FileName = prefixTokens[0];
            for (var index = 1; index < prefixTokens.Count; index++)
            {
                startInfo.ArgumentList.Add(prefixTokens[index]);
            }

            startInfo.ArgumentList.Add(scriptPath);
            foreach (var token in tokens)
            {
                startInfo.ArgumentList.Add(token);
            }

            return true;
        }

        private static bool TryBuildTerminalStartInfo(
            string? commandPrefix,
            string scriptPath,
            string workingDirectory,
            List<string> tokens,
            out ProcessStartInfo? startInfo)
        {
            startInfo = null;

            if (!TryParseCommandPrefix(commandPrefix, out var prefixTokens))
            {
                return false;
            }

            var commandTokens = new List<string>(prefixTokens)
            {
                scriptPath
            };
            commandTokens.AddRange(tokens);

            startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {BuildCommandLine(commandTokens)}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            };

            return true;
        }

        private static string BuildCommandLine(List<string> tokens)
        {
            return string.Join(" ", tokens.Select(FormatCommandToken));
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

        /// <summary>
        /// 查找 Python 解释器，优先使用虚拟环境。
        /// </summary>
        private static string ResolvePythonExecutable(string workingDirectory)
        {
            // 1. 检查是否有 uv 工具（优先使用 uv run）
            var uvPath = FindExecutableFromPath("uv.exe");
            if (!string.IsNullOrWhiteSpace(uvPath))
            {
                // 检查工作目录是否有 pyproject.toml 或 .python-version
                if (File.Exists(Path.Combine(workingDirectory, "pyproject.toml")) ||
                    File.Exists(Path.Combine(workingDirectory, ".python-version")))
                {
                    return uvPath; // 返回 uv，后续会用 "uv run" 执行
                }
            }

            // 2. 检查虚拟环境（.venv, venv, env）
            var venvPaths = new[]
            {
                Path.Combine(workingDirectory, ".venv", "Scripts", "python.exe"),
                Path.Combine(workingDirectory, "venv", "Scripts", "python.exe"),
                Path.Combine(workingDirectory, "env", "Scripts", "python.exe")
            };

            foreach (var venvPath in venvPaths)
            {
                if (File.Exists(venvPath))
                {
                    return venvPath;
                }
            }

            // 3. 检查 VIRTUAL_ENV 环境变量
            var virtualEnv = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
            if (!string.IsNullOrWhiteSpace(virtualEnv))
            {
                var virtualEnvPython = Path.Combine(virtualEnv, "Scripts", "python.exe");
                if (File.Exists(virtualEnvPython))
                {
                    return virtualEnvPython;
                }
            }

            // 4. 使用系统 Python
            var systemPython = FindExecutableFromPath("python.exe");
            if (!string.IsNullOrWhiteSpace(systemPython))
            {
                return systemPython;
            }

            var python3 = FindExecutableFromPath("python3.exe");
            if (!string.IsNullOrWhiteSpace(python3))
            {
                return python3;
            }

            return string.Empty;
        }

        /// <summary>
        /// 查找 Node.js 解释器，优先使用项目本地的 node_modules。
        /// </summary>
        private static string ResolveNodeExecutable(string workingDirectory)
        {
            // 1. 检查是否有 package.json（Node.js 项目）
            if (File.Exists(Path.Combine(workingDirectory, "package.json")))
            {
                // 检查本地 node_modules/.bin
                var localNode = Path.Combine(workingDirectory, "node_modules", ".bin", "node.exe");
                if (File.Exists(localNode))
                {
                    return localNode;
                }
            }

            // 2. 检查 nvm（Node Version Manager）
            var nvmPath = Environment.GetEnvironmentVariable("NVM_HOME");
            if (!string.IsNullOrWhiteSpace(nvmPath))
            {
                var nvmNode = Path.Combine(nvmPath, "node.exe");
                if (File.Exists(nvmNode))
                {
                    return nvmNode;
                }
            }

            // 3. 使用系统 Node.js
            return FindExecutableFromPath("node.exe");
        }

        /// <summary>
        /// 查找 Ruby 解释器，支持 rbenv 等版本管理工具。
        /// </summary>
        private static string ResolveRubyExecutable(string workingDirectory)
        {
            // 1. 检查 .ruby-version 文件
            var rubyVersionFile = Path.Combine(workingDirectory, ".ruby-version");
            if (File.Exists(rubyVersionFile))
            {
                // 如果有 rbenv 或 rvm，优先使用
                var rbenv = FindExecutableFromPath("rbenv.bat");
                if (!string.IsNullOrWhiteSpace(rbenv))
                {
                    return FindExecutableFromPath("ruby.exe"); // rbenv 会自动处理版本
                }
            }

            // 2. 使用系统 Ruby
            return FindExecutableFromPath("ruby.exe");
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

        /// <summary>
        /// 动态自适应按行字节读取器：直接以 \n 分离，对整行原始 byte 调用 DecodeProcessOutput 自适应双解码。
        /// </summary>
        private static async Task ReadStreamLineByLineAsync(Stream stream, Action<string> onLineAction)
        {
            var buffer = new byte[4096];
            var byteList = new List<byte>();
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    // 分支注释：当遇到换行符 \n (0x0A) 时，对一整行未加工的 byte[] 原始数据进行自适应解码
                    if (b == 0x0A)
                    {
                        var lineBytes = byteList.ToArray();
                        byteList.Clear();

                        // 移除末尾存在的 \r 回车符 (0x0D)
                        if (lineBytes.Length > 0 && lineBytes[^1] == 0x0D)
                        {
                            lineBytes = lineBytes[..^1];
                        }

                        string decodedLine = DecodeProcessOutput(lineBytes);
                        onLineAction(decodedLine);
                    }
                    else
                    {
                        byteList.Add(b);
                    }
                }
            }

            // 分支注释：处理流末尾没有以 \n 结尾的遗存残渣数据
            if (byteList.Count > 0)
            {
                string decodedLine = DecodeProcessOutput(byteList.ToArray());
                onLineAction(decodedLine);
            }
        }
    }
}
