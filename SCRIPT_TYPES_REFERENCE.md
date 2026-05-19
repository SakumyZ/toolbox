# 脚本类型快速参考

## 支持的脚本类型一览表

| 脚本类型 | 文件扩展名 | 解释器 | 虚拟环境支持 | 版本管理 |
|---------|-----------|--------|------------|---------|
| **Batch** | `.bat`, `.cmd` | `cmd.exe` | ❌ | ❌ |
| **PowerShell** | `.ps1` | `pwsh.exe` / `powershell.exe` | ❌ | ❌ |
| **Shell** | `.sh` | `bash.exe` / `sh.exe` | ❌ | ❌ |
| **Python** | `.py` | `python.exe` / `python3.exe` | ✅ | ✅ uv |
| **Node.js** | `.js`, `.mjs`, `.cjs` | `node.exe` | ✅ | ✅ nvm |
| **Ruby** | `.rb` | `ruby.exe` | ❌ | ✅ rbenv |
| **Perl** | `.pl` | `perl.exe` | ❌ | ❌ |
| **PHP** | `.php` | `php.exe` | ❌ | ❌ |
| **Lua** | `.lua` | `lua.exe` / `lua53.exe` | ❌ | ❌ |

## 环境检测优先级

### Python
```
1. uv run (如果有 pyproject.toml 或 .python-version)
2. .venv/Scripts/python.exe
3. venv/Scripts/python.exe
4. env/Scripts/python.exe
5. VIRTUAL_ENV 环境变量
6. 系统 Python
```

### Node.js
```
1. node_modules/.bin/node.exe
2. NVM_HOME/node.exe
3. 系统 Node.js
```

### Ruby
```
1. rbenv (如果有 .ruby-version)
2. 系统 Ruby
```

## 常用命令示例

### Python
```bash
# 直接执行
python script.py --arg1 value1 --arg2 value2

# uv 项目
uv run script.py --arg1 value1

# 虚拟环境
.venv\Scripts\python.exe script.py
```

### Node.js
```bash
# 直接执行
node script.js arg1 arg2

# npm 脚本
npm run script-name
```

### Ruby
```bash
# 直接执行
ruby script.rb arg1 arg2

# rbenv
rbenv exec ruby script.rb
```

## 参数传递格式

### 命名参数
```
--name value
--count 5
--verbose true
```

### 位置参数
```
arg1 arg2 arg3
```

### 布尔参数
```
--flag true
--flag false
```

## 工作目录配置

- **留空**：使用脚本所在目录
- **相对路径**：相对于脚本所在目录
- **绝对路径**：指定的完整路径

## 终端运行模式

### 直接运行
- 在 ToolBox 内执行
- 查看完整输出
- 适合非交互式脚本

### 新终端运行
- 在新 cmd 窗口执行
- 支持用户输入
- 适合交互式脚本

## 快速上手

1. **导入脚本**：点击"导入脚本"选择文件
2. **配置参数**：添加需要的参数定义
3. **设置默认值**：为参数设置默认值
4. **保存脚本**：保存到 ToolBox
5. **运行测试**：点击"运行脚本"测试

## 故障排查

### 脚本无法运行
- ✅ 检查解释器是否安装
- ✅ 确认解释器在 PATH 中
- ✅ 验证脚本文件存在
- ✅ 检查工作目录设置

### 虚拟环境未识别
- ✅ 确认虚拟环境目录名称（.venv, venv, env）
- ✅ 检查虚拟环境是否完整
- ✅ 验证 python.exe 存在

### 参数传递错误
- ✅ 检查参数名称格式
- ✅ 确认参数类型匹配
- ✅ 验证必填参数已填写

## 最佳实践

1. **使用虚拟环境**（Python）
2. **配置 .ruby-version**（Ruby）
3. **使用 package.json**（Node.js）
4. **添加参数说明**
5. **设置合理的默认值**
6. **测试后再使用**

## 环境要求

| 语言 | 最低版本 | 推荐版本 | 安装方式 |
|-----|---------|---------|---------|
| Python | 3.7+ | 3.11+ | python.org / uv |
| Node.js | 14+ | 20+ | nodejs.org / nvm |
| Ruby | 2.7+ | 3.2+ | rubyinstaller.org |
| Perl | 5.x | 5.36+ | strawberryperl.com |
| PHP | 7.4+ | 8.2+ | php.net |
| Lua | 5.1+ | 5.4+ | luabinaries.org |
