# 自定义解释器路径功能

## ✨ 新功能说明

现在脚本管理器支持为每个脚本配置自定义解释器路径，让你可以：

1. **指定特定版本的解释器**
2. **使用虚拟环境中的解释器**
3. **使用自定义工具（如 uv）**
4. **覆盖自动检测的解释器**

## 🎯 使用场景

### 场景 1：使用特定 Python 版本
```
自定义解释器路径：C:\Python39\python.exe
```
强制使用 Python 3.9，而不是系统默认的 Python 3.11

### 场景 2：使用 uv 运行 Python 脚本
```
自定义解释器路径：uv
```
或
```
自定义解释器路径：C:\Users\user\.cargo\bin\uv.exe
```

### 场景 3：使用虚拟环境
```
自定义解释器路径：D:\project\.venv\Scripts\python.exe
```

### 场景 4：使用 Node.js 特定版本
```
自定义解释器路径：C:\Program Files\nodejs-v18\node.exe
```

## 📝 配置方法

### 1. 在脚本编辑界面

找到"自定义解释器路径（可选）"输入框：

```
┌─────────────────────────────────────────────┐
│ 自定义解释器路径（可选）                      │
│ ┌─────────────────────────────────────────┐ │
│ │ 留空则自动检测，例如：                    │ │
│ │ C:\Python311\python.exe 或 uv           │ │
│ └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

### 2. 输入解释器路径

**完整路径示例：**
- `C:\Python311\python.exe`
- `C:\Program Files\nodejs\node.exe`
- `C:\Ruby32-x64\bin\ruby.exe`

**简短命令示例：**
- `python`
- `python3`
- `uv`
- `node`

### 3. 保存脚本

点击"保存脚本"按钮，自定义解释器路径会被保存。

## 🔍 工作原理

### 解释器选择优先级

```
1. 自定义解释器路径（如果已配置）
   ↓
2. 自动检测的解释器
   ↓
3. 系统 PATH 中的解释器
```

### 命令预览示例

#### 留空（自动检测）
```bash
# Python 脚本
"C:\Users\user\AppData\Local\Microsoft\WindowsApps\python.exe" "C:\path\to\script.py" arg1

# 如果检测到 uv 项目
"C:\Users\user\.cargo\bin\uv.exe" run "C:\path\to\script.py" arg1
```

#### 配置为 "uv"
```bash
uv run "C:\path\to\script.py" arg1
```

#### 配置为完整路径
```bash
"C:\Python39\python.exe" "C:\path\to\script.py" arg1
```

## 💡 使用技巧

### 1. Python 项目

**使用 uv（推荐）：**
```
自定义解释器路径：uv
```
命令预览：`uv run script.py`

**使用特定虚拟环境：**
```
自定义解释器路径：D:\project\.venv\Scripts\python.exe
```

**使用系统 Python 3.9：**
```
自定义解释器路径：C:\Python39\python.exe
```

### 2. Node.js 项目

**使用 NVM 管理的特定版本：**
```
自定义解释器路径：C:\Users\user\AppData\Roaming\nvm\v18.17.0\node.exe
```

**使用项目本地的 Node.js：**
```
自定义解释器路径：D:\project\node_modules\.bin\node.exe
```

### 3. Ruby 项目

**使用 rbenv 管理的特定版本：**
```
自定义解释器路径：C:\Users\user\.rbenv\versions\3.2.0\bin\ruby.exe
```

## 🎨 UI 界面

### 基础信息区域

```
┌─────────────────────────────────────────────────────┐
│ 基础信息                                             │
│ 脚本文件、说明和工作目录配置                          │
├─────────────────────────────────────────────────────┤
│                                                      │
│ 脚本名称：[Excel转Markdown                    ]      │
│ 脚本类型：[Python          ▼]                       │
│                                                      │
│ 脚本说明：                                           │
│ ┌──────────────────────────────────────────┐        │
│ │ Excel 表格转换为 Markdown 文档            │        │
│ └──────────────────────────────────────────┘        │
│                                                      │
│ 脚本文件：[C:\...\excel_to_md.py          ]         │
│           [导入脚本] [打开目录]                      │
│                                                      │
│ 工作目录：[D:\Projects\...\frontend       ]         │
│                                                      │
│ 自定义解释器路径（可选）：                           │
│ ┌──────────────────────────────────────────┐        │
│ │ uv                                        │        │
│ └──────────────────────────────────────────┘        │
│ 留空则自动检测，例如：C:\Python311\python.exe       │
│                                                      │
└─────────────────────────────────────────────────────┘
```

## 📊 数据库结构

### Scripts 表新增字段

```sql
ALTER TABLE Scripts
ADD COLUMN CustomInterpreterPath TEXT NOT NULL DEFAULT '';
```

### 字段说明

- **字段名**：`CustomInterpreterPath`
- **类型**：`TEXT`
- **默认值**：空字符串
- **说明**：用户自定义的解释器路径，留空则使用自动检测

## 🔄 兼容性

### 向后兼容

- ✅ 现有脚本自动添加空的 `CustomInterpreterPath` 字段
- ✅ 留空时行为与之前完全一致（自动检测）
- ✅ 不影响现有脚本的执行

### 数据库升级

应用启动时会自动执行：
```csharp
EnsureColumnExists(connection, "Scripts", "CustomInterpreterPath", "TEXT NOT NULL DEFAULT ''");
```

## ✅ 测试步骤

### 1. 测试自动检测（留空）

1. 创建 Python 脚本
2. 不填写"自定义解释器路径"
3. 查看命令预览
4. 确认显示自动检测的解释器

### 2. 测试自定义路径

1. 填写"自定义解释器路径"：`uv`
2. 查看命令预览
3. 确认显示：`uv run script.py`

### 3. 测试完整路径

1. 填写完整路径：`C:\Python39\python.exe`
2. 查看命令预览
3. 确认显示：`"C:\Python39\python.exe" "script.py"`

### 4. 测试保存和加载

1. 保存脚本
2. 切换到其他脚本
3. 切换回来
4. 确认自定义解释器路径被正确加载

## 🚀 下一步

1. 关闭当前运行的 ToolBox
2. 运行新构建的版本
3. 打开现有的 Python 脚本
4. 在"自定义解释器路径"中输入 `uv`
5. 保存脚本
6. 查看命令预览，应该显示：`uv run ...`

## 📝 注意事项

1. **路径格式**：
   - Windows 路径使用反斜杠 `\`
   - 包含空格的路径会自动添加引号

2. **简短命令**：
   - 如果只输入命令名（如 `uv`、`python`），系统会在 PATH 中查找
   - 如果找不到，脚本执行会失败

3. **uv 特殊处理**：
   - 输入 `uv` 会自动添加 `run` 子命令
   - 完整路径 `C:\...\uv.exe` 也会自动添加 `run`

4. **验证**：
   - 保存前可以通过"命令预览"查看最终命令
   - 确保命令格式正确后再保存
