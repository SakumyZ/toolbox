# 实施计划 - 命令行工具 't' (第一阶段：SSH 预设)

本计划书详细阐述了命令行工具 `t` 第一阶段的设计与实装方案。本阶段将完全聚焦于 **SSH 预设管理器** 的命令行子系统，全部采用中文说明，且文件存放于 `docs` 目录下。

## 目标说明
使开发人员与 Agent 在终端开发过程中遇到 SSH 权限冲突（例如由于角色不符导致 Git Push 失败）时，能够直接通过命令行快速查看并切换 SSH 预设，从而避免频繁打断工作流去启动 ToolBox 桌面 GUI。

---

## 命令行交互设计

所有命令均使用别名 `t` 激活：

### 1. `t ssh list` (或简写为 `t ssh`)
* **作用**：列出数据库中所有已保存的 SSH 预设配置，并在列表最上方清晰呈现当前生效的预设。
* **输出格式示例**：
  ```text
  Currently using SSH preset: Work

  ID   Name        Description                 Last Used
  ----------------------------------------------------------------------------
  1    Personal    Github 个人账号配置         2026-07-13 18:00
  2    Work        公司内部 GitLab 配置         2026-07-14 12:30
  ```
  *(注：如果数据库中当前没有预设被标记为激活，最上方将输出 `Currently using SSH preset: None / Externally Modified`)*

### 2. `t ssh use <预设名称或ID>`
* **作用**：激活指定的配置预设，安全覆盖本地系统 `~/.ssh/config`，并自动备份旧的配置文件。
* **参数支持**：
  - 支持精确输入预设数字 **ID**（例如 `t ssh use 1`）。
  - 支持输入预设 **名称**，并采用**不区分大小写的模糊匹配**。
* **模糊匹配判定逻辑**：
  - 若仅匹配到一个结果：直接激活并输出成功提示。
  - 若匹配到多个候选项：在终端中列出所有符合条件的预设名称与 ID，并提示用户输入更具体的名字。
  - 若未匹配到任何结果：报错提示。
* **输出格式示例**：
  ```text
  [Success] 已成功切换至预设: Work (ID: 2)
  ```

---

## 变更文件列表

### 项目结构配置
#### [NEW] tools/t/ToolBox.Cli.csproj
创建轻量级的 .NET 8 控制台项目。由于引用了包含 UI 模型依赖的 `SshConfigPreset.cs` 等文件，我们需要在项目配置中加入 `<UseWinUI>true</UseWinUI>`，以便项目完美编译，且对原有代码实现**零修改、零侵入**。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>t</AssemblyName>
    <RootNamespace>ToolBox.Cli</RootNamespace>
    <Platforms>AnyCPU;x64</Platforms>
    <UseWinUI>true</UseWinUI>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
    <PackageReference Include="System.ServiceProcess.ServiceController" Version="10.0.3" />
    <PackageReference Include="Serilog" Version="4.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
  </ItemGroup>

  <!-- 通过文件链接机制直接编译现有的业务服务与模型，避免重复造轮子 -->
  <ItemGroup>
    <Compile Include="..\..\Models\SshConfigPreset.cs" Link="Models\SshConfigPreset.cs" />
    <Compile Include="..\..\Services\SshConfigService.cs" Link="Services\SshConfigService.cs" />
  </ItemGroup>
</Project>
```

---

### 命令行逻辑分发
#### [NEW] tools/t/Program.cs
解析用户输入的命令行参数，并路由到对应的 SSH 配置读写与覆盖方法中。

```csharp
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ToolBox.Services;
using ToolBox.Models;
using Serilog;

namespace ToolBox.Cli
{
    class Program
    {
        private static readonly SshConfigService _sshService = new();

        static int Main(string[] args)
        {
            // 设定 Serilog 只输出干净的原生消息，以契合 CLI 排版风格
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}")
                .CreateLogger();

            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] subArgs = args.Skip(1).ToArray();

            try
            {
                // 分发命令
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
                    Console.WriteLine($"[Error] 未知的命令: {command}。运行 't help' 查看帮助。");
                    return 1;
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

        private static int PrintHelp()
        {
            Console.WriteLine("ToolBox CLI 助手 't' - 使用说明:");
            Console.WriteLine("  t ssh list                      列出所有已保存的 SSH 预设");
            Console.WriteLine("  t ssh use <名称或ID>            一键切换系统 SSH config 为指定预设");
            return 0;
        }

        private static int HandleSsh(string[] args)
        {
            string subCommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

            // 分发 ssh 子命令
            return subCommand switch
            {
                "list" => ListPresets(),
                "use" => UsePreset(args.Skip(1).ToArray()),
                _ => UnknownSshCommand(subCommand)
            };
        }

        private static int ListPresets()
        {
            var presets = _sshService.GetAllPresets();
            // 分支注释：若当前没有任何预设
            if (presets.Count == 0)
            {
                Console.WriteLine("尚未配置任何 SSH 预设。您可以在 ToolBox 桌面应用中创建它们。");
                return 0;
            }

            // 获取当前处于激活态的预设
            var activePreset = presets.FirstOrDefault(p => p.IsActive);
            string activeName = activePreset != null ? activePreset.Name : "None / Externally Modified";

            Console.WriteLine($"Currently using SSH preset: {activeName}\n");
            Console.WriteLine($"{"ID",-4} {"Name",-20} {"Description",-30} {"Last Used",-16}");
            Console.WriteLine(new string('-', 75));

            foreach (var preset in presets)
            {
                string name = preset.Name.Length > 18 ? preset.Name.Substring(0, 15) + "..." : preset.Name;
                string desc = preset.Description.Length > 28 ? preset.Description.Substring(0, 25) + "..." : preset.Description;
                string lastUsed = preset.LastUsedAt?.ToString("yyyy-MM-dd HH:mm") ?? "从未使用";

                Console.WriteLine($"{preset.Id,-4} {name,-20} {desc,-30} {lastUsed,-16}");
            }
            return 0;
        }

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

            // 分支注释：首先尝试作为 ID (数字) 进行精确查找
            if (long.TryParse(target, out long targetId))
            {
                selected = presets.FirstOrDefault(p => p.Id == targetId);
            }

            // 分支注释：如果不是 ID，进行不区分大小写模糊名称匹配
            if (selected == null)
            {
                var matches = presets.Where(p => p.Name.Contains(target, StringComparison.OrdinalIgnoreCase)).ToList();

                // 分支注释：正好匹配到一个
                if (matches.Count == 1)
                {
                    selected = matches[0];
                }
                // 分支注释：匹配到多个，要求用户输入更精准的词
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

        private static int UnknownSshCommand(string cmd)
        {
            Console.WriteLine($"[Error] 未知的 SSH 子命令: '{cmd}'。可用子命令: list, use。");
            return 1;
        }
    }
}
```

---

### 无感环境注册与更新
#### [MODIFY] MainWindow.xaml.cs
在桌面主应用加载时，自动异步检测当前的命令行可执行文件路径是否已存在于用户的 `PATH` 环境变量中，若不存在则在后台静默添加，彻底做到无感化。

```csharp
        // 在 MainWindow.xaml.cs 的构造函数中追加调用：
        public MainWindow()
        {
            InitializeComponent();
            // ... 原有逻辑
            RegisterCliPath(); // 启动时异步注册 CLI 环境变量
        }

        /// <summary>
        /// 异步检查并自动注册当前应用 CLI 工具（t.exe）路径到系统用户级 PATH 环境中。
        /// </summary>
        private void RegisterCliPath()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                    var targetPaths = new List<string> { currentDir };

                    // 分支注释：如果是 Debug 调试环境，额外将 tools\t 的 Debug 输出目录也加入判断与注册范围
                    if (currentDir.Contains(@"\bin\Debug\"))
                    {
                        string projectRoot = Path.GetFullPath(Path.Combine(currentDir, @"..\..\..\.."));
                        string debugCliPath = Path.Combine(projectRoot, @"tools\t\bin\Debug\net8.0-windows10.0.19041.0\win-x64");
                        if (Directory.Exists(debugCliPath))
                        {
                            targetPaths.Add(debugCliPath);
                        }
                    }

                    string? userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                    userPath ??= string.Empty;

                    var existingPaths = userPath
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.TrimEnd('\\'))
                        .ToList();

                    bool needUpdate = false;
                    var pathsToAdd = new List<string>();

                    foreach (var targetPath in targetPaths)
                    {
                        // 分支注释：若用户 PATH 中不包含此路径，则添加
                        if (!existingPaths.Any(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            pathsToAdd.Add(targetPath);
                            needUpdate = true;
                        }
                    }

                    if (needUpdate)
                    {
                        string newPath = userPath;
                        foreach (var path in pathsToAdd)
                        {
                            newPath = newPath.TrimEnd(';') + ";" + path;
                            Log.Information("ToolBox CLI 自动向用户环境变量 PATH 注册了新路径: {Path}", path);
                        }
                        Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.User);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ToolBox CLI 自动注册用户 PATH 环境变量失败");
                }
            });
        }
```

---

## 验证计划

### 构建验证
* 在项目根目录下，利用命令行验证主项目与 CLI 项目的编译完整性：
  ```powershell
  dotnet build -r win-x64
  dotnet build tools/t/ToolBox.Cli.csproj -r win-x64
  ```

### 手动功能验证
1. 打开 ToolBox 桌面应用，通过控制台日志或在系统高级属性中检查，确认 `tools\t\bin\...` 路径已被**自动、静默地追加**到了用户 `Path` 变量中，且没有发生重复追加。
2. 打开新的命令行终端，输入：
   - `t ssh list` -> 验证列表上方能正确读出当前正生效的预设。
   - `t ssh use <另一个预设名称>` -> 验证一键切换成功，且再次查看 `t ssh list` 时顶部状态更新。
