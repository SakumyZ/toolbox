# 技术实现总结 - 命令行工具 't' (第一阶段：SSH 预设)

我们已经成功实装并测试通过了轻量、全局免重启的 ToolBox CLI 助手 `t.exe`，并完美实现了对系统 **SSH 预设切换** 的秒级命令行自动化管理。

---

## 改动详情

### 1. CLI 专属控制台项目
* **[ToolBox.Cli.csproj](file:///D:/Projects/Personal/toolbox/tools/t/ToolBox.Cli.csproj)**:
  - 新建控制台项目，通过链接机制完美复用了主程序的 `SshConfigService.cs` 和 `SshConfigPreset.cs`。
  - 启用了 `<UseWinUI>true</UseWinUI>`，以完美解决对含有 UI 类型关联模型的直接编译，彻底避免修改主程序的任何已有代码。

### 2. 命令行核心控制分发与色彩美化
* **[Program.cs](file:///D:/Projects/Personal/toolbox/tools/t/Program.cs)**:
  - 实现了 `t ssh list` 列表查询。在控制台头部友好标出当前处于激活状态的预设，并以规整的二维表格列出全部预设。
  - **色彩高亮 (新)**：使用 `Console.ForegroundColor` 将头部显示的激活预设名称高亮渲染为**黄色**，与表格内容形成鲜明对比，更具视觉重点和现代 CLI 美感。
  - 实现了 `t ssh use <名称或ID>` 预设切换。支持输入数据库 ID 或名字的部分字段（不区分大小写模糊匹配），完成文件备份与物理 config 覆盖。

### 3. 无感环境变量静默注册
* **[MainWindow.xaml.cs](file:///D:/Projects/Personal/toolbox/MainWindow.xaml.cs)**:
  - 舍弃了繁琐的手动脚本部署方式。在桌面主程序启动时，通过 `RegisterCliPath()` 在后台异步判断 `PATH` 是否包含 CLI 路径。
  - 若不包含则自动添加，并能智能区分开发环境 Debug 目录和发布后同级目录，做到开发与使用阶段的完全无感化。

---

## 验证与测试结果

### 1. 编译校验
* 主程序与 CLI 控制台程序分别通过 `dotnet build -r win-x64` 进行构建。
* **结果**：**0 警告，0 错误**。

### 2. 运行与功能检验
在 Windows 终端中运行：
```powershell
# 1. 查看全部 SSH 预设
t ssh list
```
**终端反馈输出**（其中 `housei` 会高亮呈现为**黄色**）：
```text
Currently using SSH preset: housei

ID   Name                 Description                    Last Used       
---------------------------------------------------------------------------
2    housei                                              2026-07-14 17:09
1    sakumyz                                             2026-07-14 17:09
```
本地系统的 `~/.ssh/config` 配置替换秒级响应，功能已全数合拢！
