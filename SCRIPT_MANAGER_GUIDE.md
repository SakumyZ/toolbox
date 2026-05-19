# ToolBox 脚本管理器使用指南

## 📋 支持的脚本类型

ToolBox 脚本管理器现在支持以下脚本语言：

### 1. **Batch (.bat, .cmd)**
- Windows 批处理脚本
- 使用 `cmd.exe` 执行

### 2. **PowerShell (.ps1)**
- Windows PowerShell 脚本
- 自动检测 `pwsh.exe`（PowerShell Core）或 `powershell.exe`（Windows PowerShell）

### 3. **Shell (.sh)**
- Unix/Linux Shell 脚本
- 在 Windows 上需要 Git Bash 或 WSL
- 自动检测 `bash.exe` 或 `sh.exe`

### 4. **Python (.py)** ✨ 新增
- Python 脚本
- **智能虚拟环境检测**：
  - 优先使用 `uv run`（如果项目有 `pyproject.toml` 或 `.python-version`）
  - 自动检测 `.venv`、`venv`、`env` 虚拟环境
  - 支持 `VIRTUAL_ENV` 环境变量
  - 回退到系统 Python

### 5. **Node.js (.js, .mjs, .cjs)** ✨ 新增
- JavaScript 脚本
- **智能项目检测**：
  - 检测 `package.json` 项目
  - 支持本地 `node_modules/.bin`
  - 支持 NVM（Node Version Manager）
  - 回退到系统 Node.js

### 6. **Ruby (.rb)** ✨ 新增
- Ruby 脚本
- 支持 `.ruby-version` 文件
- 支持 rbenv 版本管理

### 7. **Perl (.pl)** ✨ 新增
- Perl 脚本
- 使用系统 Perl 解释器

### 8. **PHP (.php)** ✨ 新增
- PHP 脚本
- 使用系统 PHP 解释器

### 9. **Lua (.lua)** ✨ 新增
- Lua 脚本
- 支持 `lua.exe` 或 `lua53.exe`

## 🚀 使用方法

### 导入脚本

1. 点击"新增脚本"按钮
2. 点击"导入脚本"按钮
3. 选择脚本文件（支持所有上述文件类型）
4. 脚本类型会自动识别
5. 填写脚本名称和说明
6. 点击"保存脚本"

### 配置参数

脚本管理器支持为脚本定义参数：

- **文本参数**：普通文本输入
- **多行文本**：支持换行的文本输入
- **布尔参数**：开关切换

参数配置支持：
- 参数名称和显示名称
- 默认值和占位符
- 帮助文本
- 必填/可选标记
- 排序顺序

### 运行脚本

1. **直接运行**：在 ToolBox 内执行，查看输出
2. **在新终端运行**：勾选"在新终端中运行"，适合需要交互的脚本

## 🔧 虚拟环境支持

### Python 项目

ToolBox 会自动检测并使用 Python 虚拟环境：

```bash
# 项目结构示例
my-project/
├── .venv/              # 虚拟环境（自动检测）
├── pyproject.toml      # uv 项目配置（使用 uv run）
├── .python-version     # Python 版本文件（使用 uv run）
└── script.py           # 你的脚本
```

**优先级**：
1. `uv run`（如果有 uv 且项目有配置文件）
2. `.venv/Scripts/python.exe`
3. `venv/Scripts/python.exe`
4. `env/Scripts/python.exe`
5. `VIRTUAL_ENV` 环境变量
6. 系统 Python

### Node.js 项目

```bash
# 项目结构示例
my-project/
├── node_modules/       # 依赖（自动检测）
├── package.json        # 项目配置
└── script.js           # 你的脚本
```

### Ruby 项目

```bash
# 项目结构示例
my-project/
├── .ruby-version       # Ruby 版本文件（rbenv）
└── script.rb           # 你的脚本
```

## 📝 示例脚本

### Python 示例

```python
#!/usr/bin/env python3
import sys
import argparse

def main():
    parser = argparse.ArgumentParser(description='示例脚本')
    parser.add_argument('--name', type=str, default='World')
    parser.add_argument('--count', type=int, default=1)

    args = parser.parse_args()

    for i in range(args.count):
        print(f"Hello, {args.name}!")

if __name__ == "__main__":
    main()
```

### Node.js 示例

```javascript
#!/usr/bin/env node
const args = process.argv.slice(2);
const name = args[1] || 'World';
const count = parseInt(args[3] || '1');

for (let i = 0; i < count; i++) {
    console.log(`Hello, ${name}!`);
}
```

## 🎯 最佳实践

1. **使用虚拟环境**：为 Python 项目创建虚拟环境，避免依赖冲突
2. **设置工作目录**：确保脚本在正确的目录下执行
3. **参数化脚本**：使用参数定义功能，让脚本更灵活
4. **添加帮助文本**：为参数添加说明，方便使用
5. **测试脚本**：保存后先测试运行，确保参数正确

## 🔍 故障排查

### Python 脚本无法运行

- 检查是否安装了 Python
- 确认虚拟环境是否激活
- 查看输出中的错误信息

### Node.js 脚本无法运行

- 检查是否安装了 Node.js
- 确认 `node` 命令在 PATH 中
- 检查项目依赖是否安装（`npm install`）

### 脚本找不到解释器

- 确保对应的语言环境已安装
- 检查环境变量 PATH 配置
- 尝试在命令行手动运行脚本

## 📦 环境要求

- **Python**：Python 3.x，推荐使用 uv 管理项目
- **Node.js**：Node.js 14+
- **Ruby**：Ruby 2.x+
- **Perl**：Perl 5.x+
- **PHP**：PHP 7.x+
- **Lua**：Lua 5.x+

## 🎉 新功能亮点

1. ✨ **多语言支持**：从 3 种扩展到 9 种脚本语言
2. 🔍 **智能环境检测**：自动识别虚拟环境和项目配置
3. 📁 **所有文件支持**：文件选择器支持"所有文件"选项
4. 🚀 **uv 集成**：优先使用 uv run 执行 Python 项目
5. 🎯 **版本管理支持**：支持 rbenv、nvm 等版本管理工具
