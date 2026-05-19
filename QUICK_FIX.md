# 快速修复指南

## 问题
保存脚本时出现 `System.InvalidOperationException` 错误。

## 原因
数据库缺少 `CustomInterpreterPath` 列。

## 解决方案（最简单）

### 步骤 1：关闭 ToolBox
确保 ToolBox 完全关闭（检查任务管理器）。

### 步骤 2：备份并删除数据库

在 PowerShell 中运行以下命令：

```powershell
# 备份数据库
$dbPath = "$env:LOCALAPPDATA\ToolBox\snippets.db"
$backupPath = "$dbPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Copy-Item $dbPath $backupPath
Write-Host "已备份到: $backupPath"

# 删除数据库
Remove-Item $dbPath
Write-Host "已删除数据库"
```

或者直接在文件资源管理器中：
1. 打开：`C:\Users\<你的用户名>\AppData\Local\ToolBox\`
2. 复制 `snippets.db` 为 `snippets.db.backup`
3. 删除 `snippets.db`

### 步骤 3：重启 ToolBox

运行新构建的 Release 版本：
```
bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\ToolBox.exe
```

数据库会自动重新创建，包含所有新列。

### 步骤 4：重新导入脚本

由于数据库被删除，你需要重新导入脚本：
1. 脚本文件仍然在 `C:\Users\<你的用户名>\AppData\Local\ToolBox\Scripts\` 目录中
2. 在 ToolBox 中点击"新增脚本"
3. 点击"导入脚本"，选择之前的脚本文件
4. 重新配置参数

## 注意事项

- ✅ 脚本文件不会丢失（它们在 Scripts 文件夹中）
- ❌ 脚本配置会丢失（需要重新配置参数）
- ✅ 备份文件可以用于恢复（如果需要）

## 如果不想丢失配置

如果你有很多脚本配置不想重新设置，可以等待我们提供数据库迁移工具，或者：

1. 使用 SQLite 工具手动添加列：
   ```sql
   ALTER TABLE Scripts ADD COLUMN CustomInterpreterPath TEXT NOT NULL DEFAULT '';
   ```

2. 下载 SQLite 工具：https://www.sqlite.org/download.html

## 验证修复

1. 启动 ToolBox
2. 创建或打开一个 Python 脚本
3. 在"自定义解释器路径"中输入 `uv`
4. 点击"保存脚本"
5. 如果没有错误，说明修复成功
6. 查看命令预览，应该显示 `uv run ...`
