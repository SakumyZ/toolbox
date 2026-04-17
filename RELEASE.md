# ToolBox Release Flow

## 当前版本

- 版本号：v1.0.2
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

## 回滚说明

1. 如果仅本地发布失败，修复问题后重新执行 dotnet publish。
2. 如果标签已创建但 Release 需要重做，可先删除 GitHub Release，再删除标签并重新创建。
3. 如果已推送版本提交，回滚前先确认是否已有用户使用该版本。
