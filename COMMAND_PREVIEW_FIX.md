# 命令预览修复说明

## 🐛 问题描述

之前的命令预览对于 Python 和其他新增的脚本类型，只显示脚本路径，没有显示解释器命令：

### 错误示例：
```bash
# Python 脚本 - 错误
C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py arg1 arg2

# 应该是：
python C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py arg1 arg2
# 或者（如果使用 uv）：
uv run C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py arg1 arg2
```

## ✅ 修复方案

### 修改的方法

1. **重构 `BuildCommandPreview` 方法**
   - 添加工作目录解析
   - 调用新的 `BuildCommandParts` 方法

2. **新增 `BuildCommandParts` 方法**
   - 根据脚本类型构建完整的命令行
   - 包含解释器路径
   - 支持所有 9 种脚本类型

### 修复后的命令预览格式

#### Batch (.bat, .cmd)
```bash
C:\path\to\script.bat arg1 arg2
```

#### PowerShell (.ps1)
```powershell
& "C:\path\to\script.ps1" -Param1 value1
```

#### Shell (.sh)
```bash
"C:\Program Files\Git\usr\bin\bash.exe" /path/to/script.sh arg1
```

#### Python (.py)
```bash
# 系统 Python
"C:\Python311\python.exe" "C:\path\to\script.py" --arg1 value1

# 虚拟环境
"C:\project\.venv\Scripts\python.exe" "C:\path\to\script.py" --arg1 value1

# uv 项目
"C:\Users\user\.cargo\bin\uv.exe" run "C:\path\to\script.py" --arg1 value1
```

#### Node.js (.js, .mjs, .cjs)
```bash
# 系统 Node.js
"C:\Program Files\nodejs\node.exe" "C:\path\to\script.js" arg1

# NVM
"C:\Users\user\AppData\Roaming\nvm\v20.0.0\node.exe" "C:\path\to\script.js" arg1
```

#### Ruby (.rb)
```bash
"C:\Ruby32-x64\bin\ruby.exe" "C:\path\to\script.rb" arg1
```

#### Perl (.pl)
```bash
"C:\Strawberry\perl\bin\perl.exe" "C:\path\to\script.pl" arg1
```

#### PHP (.php)
```bash
"C:\php\php.exe" "C:\path\to\script.php" arg1
```

#### Lua (.lua)
```bash
"C:\Program Files\Lua\lua.exe" "C:\path\to\script.lua" arg1
```

## 🔍 技术细节

### BuildCommandParts 方法逻辑

```csharp
private static List<string> BuildCommandParts(
    string scriptType,
    string scriptPath,
    string workingDirectory,
    List<string> tokens)
{
    var parts = new List<string>();

    switch (scriptType)
    {
        case ScriptTypes.Python:
            var pythonExe = ResolvePythonExecutable(workingDirectory);
            if (pythonExe.EndsWith("uv.exe"))
            {
                parts.Add(QuoteCommandPart(pythonExe));
                parts.Add("run");
            }
            else
            {
                parts.Add(QuoteCommandPart(pythonExe));
            }
            parts.Add(QuoteCommandPart(scriptPath));
            break;

        // ... 其他脚本类型
    }

    // 添加参数
    foreach (var token in tokens)
    {
        parts.Add(QuoteCommandPart(token));
    }

    return parts;
}
```

### 智能解释器解析

- **Python**：使用 `ResolvePythonExecutable(workingDirectory)` 智能检测
- **Node.js**：使用 `ResolveNodeExecutable(workingDirectory)` 智能检测
- **Ruby**：使用 `ResolveRubyExecutable(workingDirectory)` 智能检测
- **其他**：使用 `FindExecutableFromPath()` 查找系统解释器

### 路径引号处理

所有路径都通过 `QuoteCommandPart()` 方法处理：
- 包含空格的路径自动添加引号
- 特殊字符正确转义
- 确保命令行可以直接复制使用

## 📝 使用示例

### Python 脚本示例

**脚本配置：**
- 脚本类型：Python
- 脚本路径：`C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py`
- 参数1：输入文件路径
- 参数2：输出目录

**命令预览（系统 Python）：**
```bash
"C:\Python311\python.exe" "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" "C:\Users\housei\Desktop\input.xlsx" "D:\output\"
```

**命令预览（uv 项目）：**
```bash
"C:\Users\housei\.cargo\bin\uv.exe" run "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" "C:\Users\housei\Desktop\input.xlsx" "D:\output\"
```

### Node.js 脚本示例

**脚本配置：**
- 脚本类型：Node.js
- 脚本路径：`C:\project\scripts\build.js`
- 参数：--mode production

**命令预览：**
```bash
"C:\Program Files\nodejs\node.exe" "C:\project\scripts\build.js" --mode production
```

## ✅ 验证方法

1. **打开 ToolBox**
2. **选择一个 Python 脚本**
3. **查看"命令预览"区域**
4. **确认显示格式**：
   - ✅ 包含 Python 解释器路径
   - ✅ 包含脚本路径（带引号）
   - ✅ 包含所有参数
5. **复制命令到终端测试**
6. **验证命令可以直接执行**

## 🎯 修复效果

### 修复前
```bash
C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py arg1 arg2
# ❌ 无法直接在 cmd 中执行
```

### 修复后
```bash
"C:\Python311\python.exe" "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" arg1 arg2
# ✅ 可以直接复制到 cmd 执行
```

## 📊 影响范围

- ✅ Python 脚本命令预览
- ✅ Node.js 脚本命令预览
- ✅ Ruby 脚本命令预览
- ✅ Perl 脚本命令预览
- ✅ PHP 脚本命令预览
- ✅ Lua 脚本命令预览
- ✅ Shell 脚本命令预览
- ✅ Batch 脚本命令预览（无变化）
- ✅ PowerShell 脚本命令预览（无变化）

## 🔄 兼容性

- ✅ 向后兼容：现有脚本不受影响
- ✅ 命令格式：符合 Windows cmd 规范
- ✅ 路径处理：正确处理空格和特殊字符
- ✅ 虚拟环境：自动检测并使用正确的解释器

## 🚀 下一步

1. 测试所有脚本类型的命令预览
2. 验证复制的命令可以直接执行
3. 确认虚拟环境路径正确显示
4. 收集用户反馈
