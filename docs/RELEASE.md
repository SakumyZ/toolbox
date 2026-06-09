# ToolBox Release Flow

## 当前版本

- 版本号：v1.1.1
- 推荐产物：win-x64 Release 构建包

## 本地发布步骤

1. 确认工作区干净。
2. 更新版本号：
   - ToolBox.csproj 中的 Version、AssemblyVersion、FileVersion、InformationalVersion
   - Package.appxmanifest 中的 Identity Version
3. 执行发布：

```powershell
dotnet build -c Release -p:Platform=x64
```

4. 发布目录默认位于：

```text
bin\x64\Release\net8.0-windows10.0.19041.0\win-x64
```

5. 将发布目录压缩为 zip，例如：

```powershell
Compress-Archive -Path .\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\* -DestinationPath .\dist\ToolBox-v1.0.2-win-x64.zip -Force
```

## 说明

当前项目在 `dotnet publish` 产物下会触发 Windows App SDK 启动时异常，而普通 `Release build` 输出已验证可以正常启动并渲染窗口。因此当前版本的 zip 分发以 `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64` 为准。

## GitHub Release 步骤

1. 提交版本变更并推送到远端主分支。
   - 需要当前 GitHub 账号对 `SakumyZ/toolbox` 具备 `push` 权限。
2. 创建标签：

```powershell
git tag v1.0.2
git push origin v1.0.2
```

3. 创建 Release 并上传产物：

```powershell
gh release create v1.0.2 .\dist\ToolBox-v1.0.2-win-x64.zip --repo SakumyZ/toolbox --title "v1.0.2" --notes "ToolBox v1.0.2 release"
```

也可以直接执行仓库内脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\Assets\Scripts\release.ps1 -Version v1.0.2
```

## Release Notes 模板

创建 GitHub Release 时，使用以下模板编写发布说明：

```markdown
## ✨ 新功能

- **功能名称**: 功能描述
- **功能名称**: 功能描述

## 🔧 改进

- 改进项描述
- 改进项描述

## 🐛 Bug 修复

- 修复的问题描述
- 修复的问题描述

## 📦 安装说明

1. 下载 `ToolBox-v{VERSION}-win-x64.zip`
2. 解压到任意目录
3. 运行 `ToolBox.exe` 启动应用

## 📋 系统要求

- Windows 10 版本 1809 (Build 17763) 或更高版本
- .NET 8.0 运行时（应用已包含）
- x64 架构处理器

## 🐛 已知问题

- 已知问题描述（如果有）

---

**完整更新日志**: https://github.com/SakumyZ/toolbox/compare/v{PREV_VERSION}...v{VERSION}
```

### 使用示例

```powershell
gh release create v1.0.3 .\dist\ToolBox-v1.0.3-win-x64.zip --repo SakumyZ/toolbox --title "ToolBox v1.0.3" --notes "## ✨ 新功能

- **仪表板更新**: 优化了仪表板界面和交互体验
- **脚本管理器**: 新增脚本管理功能，支持脚本的创建、编辑和执行

## 🔧 改进

- 优化了整体性能和稳定性
- 改进了用户界面的响应速度

## 📦 安装说明

1. 下载 \`ToolBox-v1.0.3-win-x64.zip\`
2. 解压到任意目录
3. 运行 \`ToolBox.exe\` 启动应用

## 📋 系统要求

- Windows 10 版本 1809 (Build 17763) 或更高版本
- .NET 8.0 运行时（应用已包含）
- x64 架构处理器

## 🐛 已知问题

- 构建过程中存在 4 个编译警告（不影响功能使用）

---

**完整更新日志**: https://github.com/SakumyZ/toolbox/compare/v1.0.2...v1.0.3"
```

## 回滚说明

1. 如果仅本地发布失败，修复问题后重新执行 dotnet publish。
2. 如果标签已创建但 Release 需要重做，可先删除 GitHub Release，再删除标签并重新创建。
3. 如果已推送版本提交，回滚前先确认是否已有用户使用该版本。
