using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ToolBox.Services;
using ToolBox.Models;
using Serilog;

namespace ToolBox.Cli
{
    /// <summary>
    /// ToolBox 命令行快捷工具 't' 的分发和入口程序。
    /// </summary>
    class Program
    {
        private static readonly SshConfigService _sshService = new();
        private static readonly ScriptManagerService _scriptService = new();

        /// <summary>
        /// 应用程序入口点。
        /// </summary>
        /// <param name="args">命令行参数列表。</param>
        /// <returns>返回执行的退出码 (0 表示成功，非 0 表示失败)。</returns>
        static int Main(string[] args)
        {
            // 强行锁定控制台的输入与输出编码为 UTF-8，以防止 Emoji 降级为 ??
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 注册 .NET 额外代码页供应商以支持 CP936 (GBK) 解码
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // 设定 Serilog 只输出干净的原生消息，以契合 CLI 排版风格
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}")
                .CreateLogger();

            // 分支注释：若未传入任何参数，默认打印帮助信息并退出
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] subArgs = args.Skip(1).ToArray();

            try
            {
                // 分支注释：根据传入的一级命令分发执行路由
                if (command == "ssh")
                {
                    return HandleSsh(subArgs);
                }
                else if (command == "help" || command == "-h" || command == "--help")
                {
                    return PrintHelp();
                }
                else
                {
                    // 分支注释：如果未命中静态命令，则转入自定义脚本 CLI 动态路由匹配与执行
                    return HandleDynamicScriptCommand(args);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] 执行发生异常: {ex.Message}");
                Console.ResetColor();
                return -1;
            }
        }

        /// <summary>
        /// 打印全局帮助信息。
        /// </summary>
        /// <returns>返回 0 退出码。</returns>
        private static int PrintHelp()
        {
            Console.WriteLine("ToolBox CLI 助手 't' - 使用说明:");
            Console.WriteLine("  t ssh list                      列出所有已保存的 SSH 预设");
            Console.WriteLine("  t ssh use <名称或ID>            一键切换系统 SSH config 为指定预设");
            return 0;
        }

        /// <summary>
        /// 处理所有 ssh 命令的分发。
        /// </summary>
        /// <param name="args">ssh 命令后面的子参数。</param>
        /// <returns>执行退出码。</returns>
        private static int HandleSsh(string[] args)
        {
            string subCommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

            // 分支注释：根据 ssh 的子命令分发具体逻辑
            return subCommand switch
            {
                "list" => ListPresets(),
                "use" => UsePreset(args.Skip(1).ToArray()),
                _ => UnknownSshCommand(subCommand)
            };
        }

        /// <summary>
        /// 列出数据库中所有的预设信息，并在列表最上方清晰呈现当前生效的预设。
        /// </summary>
        /// <returns>执行退出码。</returns>
        private static int ListPresets()
        {
            // 分支注释：从数据库获取全部预设后，按 ID 升序进行重新排序以符合 CLI 表格阅读直觉
            var presets = _sshService.GetAllPresets().OrderBy(p => p.Id).ToList();
            
            // 分支注释：若当前没有任何预设
            if (presets.Count == 0)
            {
                Console.WriteLine("尚未配置任何 SSH 预设。您可以在 ToolBox 桌面应用中创建它们。");
                return 0;
            }

            // 分支注释：获取当前处于激活状态的预设
            var activePreset = presets.FirstOrDefault(p => p.IsActive);
            string activeName = activePreset != null ? activePreset.Name : "None / Externally Modified";

            Console.Write("Currently using SSH preset: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(activeName);
            Console.ResetColor();
            Console.WriteLine("\n");
            Console.WriteLine($"{"ID",-4} {"Name",-20} {"Description",-30} {"Last Used",-16}");
            Console.WriteLine(new string('-', 75));

            foreach (var preset in presets)
            {
                // 分支注释：对过长名称及描述进行截断以美化 CLI 输出排版
                string name = preset.Name.Length > 18 ? preset.Name.Substring(0, 15) + "..." : preset.Name;
                string desc = preset.Description.Length > 28 ? preset.Description.Substring(0, 25) + "..." : preset.Description;
                string lastUsed = preset.LastUsedAt?.ToString("yyyy-MM-dd HH:mm") ?? "从未使用";

                Console.WriteLine($"{preset.Id,-4} {name,-20} {desc,-30} {lastUsed,-16}");
            }
            return 0;
        }

        /// <summary>
        /// 激活指定的配置预设，安全覆盖本地系统 ~/.ssh/config，并自动备份旧的配置文件。
        /// </summary>
        /// <param name="args">切换参数 (名称或ID)。</param>
        /// <returns>执行退出码。</returns>
        private static int UsePreset(string[] args)
        {
            // 分支注释：如果没有传要切换的参数
            if (args.Length == 0)
            {
                Console.WriteLine("[Error] 缺少目标预设参数。用法: t ssh use <名称或ID>");
                return 2;
            }

            string target = string.Join(" ", args).Trim();
            var presets = _sshService.GetAllPresets();
            SshConfigPreset? selected = null;

            // 分支注释：首先尝试作为 ID (数字类型) 进行精确查找
            if (long.TryParse(target, out long targetId))
            {
                selected = presets.FirstOrDefault(p => p.Id == targetId);
            }

            // 分支注释：如果不是 ID，则执行不区分大小写的模糊名称匹配
            if (selected == null)
            {
                var matches = presets.Where(p => p.Name.Contains(target, StringComparison.OrdinalIgnoreCase)).ToList();

                // 分支注释：正好唯一匹配到一个
                if (matches.Count == 1)
                {
                    selected = matches[0];
                }
                // 分支注释：模糊匹配到多个候选项，列出它们供用户精准选择
                else if (matches.Count > 1)
                {
                    Console.WriteLine($"[Warning] 匹配到多个符合条件的预设，请输入更具体的名字:");
                    foreach (var match in matches)
                    {
                        Console.WriteLine($"  - {match.Name} (ID: {match.Id})");
                    }
                    return 3;
                }
            }

            // 分支注释：依然没找到匹配的项
            if (selected == null)
            {
                Console.WriteLine($"[Error] 未找到匹配的预设: '{target}'。您可以执行 't ssh list' 查看全部预设。");
                return 4;
            }

            // 调用后台服务切换配置并覆盖 ~/.ssh/config
            var result = _sshService.ActivatePreset(selected.Id);
            
            // 分支注释：根据底层预设激活返回结果打印成功或失败提示
            if (result.success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Success] 已成功切换至预设: {selected.Name} (ID: {selected.Id})");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] 切换失败: {result.message}");
                Console.ResetColor();
                return 5;
            }
        }

        /// <summary>
        /// 处理未知的 SSH 子命令。
        /// </summary>
        private static int UnknownSshCommand(string cmd)
        {
            Console.WriteLine($"[Error] 未知的 SSH 子命令: '{cmd}'。可用子命令: list, use。");
            return 1;
        }

        /// <summary>
        /// 动态脚本 CLI 匹配与参数解析执行逻辑。
        /// </summary>
        /// <param name="args">完整参数数组。</param>
        /// <returns>执行退出码。</returns>
        private static int HandleDynamicScriptCommand(string[] args)
        {
            var allScripts = _scriptService.GetAllScripts()
                .Where(s => !string.IsNullOrWhiteSpace(s.CliCommandPath))
                .ToList();

            ScriptDefinition? matchedScript = null;
            int matchedWordCount = 0;

            // 1. 进行最长前缀路由匹配
            foreach (var script in allScripts)
            {
                var cmdPathWords = script.CliCommandPath!
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant())
                    .ToArray();

                if (args.Length >= cmdPathWords.Length)
                {
                    bool match = true;
                    for (int i = 0; i < cmdPathWords.Length; i++)
                    {
                        if (!args[i].Equals(cmdPathWords[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }

                    // 分支注释：若前缀全部匹配成功，且当前匹配的词数比之前的候选词数长，则视为最优候选
                    if (match && cmdPathWords.Length > matchedWordCount)
                    {
                        matchedScript = script;
                        matchedWordCount = cmdPathWords.Length;
                    }
                }
            }

            // 分支注释：若最终没有找到任何匹配的脚本路径，打印提示并报错退出
            if (matchedScript == null)
            {
                Console.WriteLine($"[Error] 未知的命令: '{string.Join(" ", args)}'。运行 't help' 查看帮助。");
                return 1;
            }

            // 2. 提取除匹配的命令关键字以外的后续参数
            string[] remainingArgs = args.Skip(matchedWordCount).ToArray();

            // 3. 智能解析并映射命令行参数到脚本的参数定义中
            var parameterValues = ResolveScriptParameters(matchedScript, remainingArgs);

            if (parameterValues.Count > 0)
            {
                Console.WriteLine("[Arguments] 解析出的参数映射:");
                foreach (var kvp in parameterValues)
                {
                    var param = matchedScript.Parameters.FirstOrDefault(p => p.Id == kvp.Key);
                    string paramName = param != null ? param.Name : $"ID: {kvp.Key}";
                    Console.WriteLine($"  - {paramName}: {kvp.Value}");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"[Running] 正在运行自定义脚本: {matchedScript.Name}...");
            
            // 4. 调用底层的脚本执行引擎，并绑定标准流与错误流以实时流式输出
            var task = _scriptService.ExecuteScriptAsync(
                matchedScript,
                parameterValues,
                onOutputLine: line => Console.WriteLine(line),
                onErrorLine: line => {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(line);
                    Console.ResetColor();
                }
            );

            task.Wait();
            var result = task.Result;

            // 分支注释：根据脚本最终的运行结果，输出成功或失败，并返回对应的退出码
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Success] 脚本执行完成，退出码: {result.ExitCode}");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] 脚本执行失败，退出码: {result.ExitCode}");
                Console.ResetColor();
                return result.ExitCode;
            }
        }

        /// <summary>
        /// 智能匹配映射算法：优先基于 Flag 命名参数，再基于位置填充剩余参数。
        /// </summary>
        /// <param name="script">脚本定义模型。</param>
        /// <param name="args">终端传来的后继参数。</param>
        /// <returns>参数 ID 到值的映射字典。</returns>
        private static Dictionary<long, string> ResolveScriptParameters(ScriptDefinition script, string[] args)
        {
            var values = new Dictionary<long, string>();
            var paramList = script.Parameters.OrderBy(p => p.SortOrder).ToList();
            var boundIndices = new HashSet<int>();

            // 1. 优先根据 Flag 命名（例如 ArgumentName 为 "-dir"）在入参中查找匹配
            foreach (var param in paramList)
            {
                if (!string.IsNullOrWhiteSpace(param.ArgumentName))
                {
                    string flag = param.ArgumentName.Trim();
                    int idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
                    
                    // 分支注释：找到了匹配的 Flag 且后面跟着参数值
                    if (idx >= 0 && idx + 1 < args.Length)
                    {
                        values[param.Id] = args[idx + 1];
                        boundIndices.Add(idx);
                        boundIndices.Add(idx + 1);
                    }
                }
            }

            // 2. 将非 Flag 占用的其它所有“位置参数”按顺序赋给那些尚未获取到值的参数
            int argPointer = 0;
            foreach (var param in paramList)
            {
                if (!values.ContainsKey(param.Id))
                {
                    // 寻找下一个未被命名 Flag 占用的纯位置参数
                    while (argPointer < args.Length && boundIndices.Contains(argPointer))
                    {
                        argPointer++;
                    }

                    // 分支注释：若位置指针未越界，赋位置参数值，否则使用其默认值填充
                    if (argPointer < args.Length)
                    {
                        values[param.Id] = args[argPointer];
                        boundIndices.Add(argPointer);
                        argPointer++;
                    }
                    else
                    {
                        values[param.Id] = param.DefaultValue ?? string.Empty;
                    }
                }
            }

            return values;
        }
    }
}
