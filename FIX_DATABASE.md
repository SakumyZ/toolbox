# 数据库修复指南

## 问题描述

如果你遇到以下错误：
```
引发的异常:"System.InvalidOperationException"(位于 Microsoft.Data.Sqlite.dll 中)
```

这是因为现有的数据库没有 `CustomInterpreterPath` 列。

## 解决方案

### 方案 1：删除数据库（简单但会丢失数据）

1. 关闭 ToolBox
2. 删除数据库文件：
   ```
   C:\Users\<你的用户名>\AppData\Local\ToolBox\snippets.db
   ```
3. 重新启动 ToolBox
4. 数据库会自动重新创建，包含新列

### 方案 2：手动添加列（保留数据）

1. 关闭 ToolBox

2. 下载 SQLite 工具：
   - 访问：https://www.sqlite.org/download.html
   - 下载 "sqlite-tools-win32-x86-*.zip"
   - 解压到任意目录

3. 打开命令提示符（cmd）

4. 进入 SQLite 工具目录：
   ```cmd
   cd C:\path\to\sqlite-tools
   ```

5. 打开数据库：
   ```cmd
   sqlite3.exe "C:\Users\<你的用户名>\AppData\Local\ToolBox\snippets.db"
   ```

6. 添加列：
   ```sql
   ALTER TABLE Scripts ADD COLUMN CustomInterpreterPath TEXT NOT NULL DEFAULT '';
   ```

7. 验证列已添加：
   ```sql
   PRAGMA table_info(Scripts);
   ```

   应该看到 `CustomInterpreterPath` 列

8. 退出 SQLite：
   ```sql
   .quit
   ```

9. 重新启动 ToolBox

### 方案 3：使用 PowerShell 脚本（推荐）

创建一个 PowerShell 脚本来自动修复：

```powershell
# fix-database.ps1
$dbPath = "$env:LOCALAPPDATA\ToolBox\snippets.db"

if (!(Test-Path $dbPath)) {
    Write-Host "数据库文件不存在：$dbPath"
    exit 1
}

Write-Host "正在修复数据库：$dbPath"

# 检查是否安装了 System.Data.SQLite
try {
    Add-Type -Path "System.Data.SQLite.dll" -ErrorAction Stop
} catch {
    Write-Host "需要安装 System.Data.SQLite"
    Write-Host "请使用方案 1 或方案 2"
    exit 1
}

# 连接数据库
$connection = New-Object System.Data.SQLite.SQLiteConnection
$connection.ConnectionString = "Data Source=$dbPath"
$connection.Open()

try {
    # 检查列是否存在
    $command = $connection.CreateCommand()
    $command.CommandText = "PRAGMA table_info(Scripts)"
    $reader = $command.ExecuteReader()

    $hasColumn = $false
    while ($reader.Read()) {
        if ($reader["name"] -eq "CustomInterpreterPath") {
            $hasColumn = $true
            break
        }
    }
    $reader.Close()

    if ($hasColumn) {
        Write-Host "✅ CustomInterpreterPath 列已存在"
    } else {
        Write-Host "正在添加 CustomInterpreterPath 列..."
        $command = $connection.CreateCommand()
        $command.CommandText = "ALTER TABLE Scripts ADD COLUMN CustomInterpreterPath TEXT NOT NULL DEFAULT ''"
        $command.ExecuteNonQuery() | Out-Null
        Write-Host "✅ CustomInterpreterPath 列已添加"
    }
} finally {
    $connection.Close()
}

Write-Host "数据库修复完成！"
```

运行脚本：
```powershell
powershell -ExecutionPolicy Bypass -File fix-database.ps1
```

## 验证修复

1. 启动 ToolBox
2. 打开任意脚本
3. 在"自定义解释器路径"中输入 `uv`
4. 点击"保存脚本"
5. 如果没有错误，说明修复成功

## 预防措施

为了避免将来出现类似问题，ToolBox 的 `InitializeTables` 方法会自动检查并添加缺失的列。但如果数据库文件被锁定或损坏，可能需要手动修复。

## 常见问题

### Q: 为什么会出现这个问题？
A: 因为你使用的是旧版本的数据库，没有 `CustomInterpreterPath` 列。新版本的代码尝试读取这个列，导致错误。

### Q: 删除数据库会丢失什么？
A: 会丢失所有保存的脚本定义和参数配置。但脚本文件本身不会丢失（它们在 `Scripts` 文件夹中）。

### Q: 如何备份数据库？
A: 复制数据库文件：
```
copy "C:\Users\<你的用户名>\AppData\Local\ToolBox\snippets.db" "C:\Users\<你的用户名>\AppData\Local\ToolBox\snippets.db.backup"
```

### Q: 修复后还是有问题怎么办？
A:
1. 确认列已正确添加
2. 检查列的数据类型是否为 TEXT
3. 检查默认值是否为空字符串
4. 尝试重启 ToolBox
5. 如果还是不行，使用方案 1（删除数据库）
