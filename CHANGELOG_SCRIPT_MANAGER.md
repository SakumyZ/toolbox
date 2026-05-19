# 脚本管理器更新日志

## [未发布] - 2026-05-08

### ✨ 新增功能

#### 多语言脚本支持
- **Python (.py)**：支持 Python 脚本执行
  - 智能检测虚拟环境（.venv, venv, env）
  - 优先使用 `uv run` 执行（如果项目有 pyproject.toml）
  - 支持 VIRTUAL_ENV 环境变量

- **Node.js (.js, .mjs, .cjs)**：支持 JavaScript 脚本执行
  - 自动检测 package.json 项目
  - 支持本地 node_modules/.bin
  - 支持 NVM 版本管理

- **Ruby (.rb)**：支持 Ruby 脚本执行
  - 支持 .ruby-version 文件
  - 支持 rbenv 版本管理

- **Perl (.pl)**：支持 Perl 脚本执行

- **PHP (.php)**：支持 PHP 脚本执行

- **Lua (.lua)**：支持 Lua 脚本执行
  - 支持 lua.exe 和 lua53.exe

#### 文件选择器改进
- 添加"所有文件 (*)"选项，不再限制只能选择特定扩展名
- 文件类型过滤器包含所有支持的脚本扩展名

#### 虚拟环境智能检测
- **Python 项目**：
  - 优先级：uv run > .venv > venv > env > VIRTUAL_ENV > 系统 Python
  - 自动检测 pyproject.toml 和 .python-version 文件

- **Node.js 项目**：
  - 检测 package.json 项目
  - 支持本地 node_modules
  - 支持 NVM_HOME 环境变量

- **Ruby 项目**：
  - 检测 .ruby-version 文件
  - 支持 rbenv 版本管理

### 🔧 改进

- 脚本类型自动识别支持更多文件扩展名
- 错误提示信息更新，包含所有支持的文件类型
- 命令预览支持新增的脚本类型

### 📝 技术细节

#### 新增方法
- `ResolvePythonExecutable(string workingDirectory)`：智能查找 Python 解释器
- `ResolveNodeExecutable(string workingDirectory)`：智能查找 Node.js 解释器
- `ResolveRubyExecutable(string workingDirectory)`：智能查找 Ruby 解释器

#### 修改的方法
- `TryResolveScriptType`：支持 9 种脚本类型识别
- `BuildStartInfo`：支持新脚本类型的执行配置
- `BuildTerminalStartInfo`：支持新脚本类型的终端启动
- `BrowseScript_Click`：文件选择器支持更多文件类型
- `ResolveScriptType`：支持更多文件扩展名映射

#### 修改的模型
- `ScriptTypes` 类：新增 6 个脚本类型常量

### 📚 文档

- 新增 `SCRIPT_MANAGER_GUIDE.md`：完整的使用指南
- 包含所有支持的脚本类型说明
- 虚拟环境配置指南
- 示例脚本和最佳实践

### 🎯 使用场景

1. **Python 开发**：
   - 使用 uv 管理的 Python 项目
   - 虚拟环境中的脚本执行
   - 数据处理和自动化脚本

2. **Node.js 开发**：
   - npm 脚本的图形化执行
   - 构建工具和自动化任务
   - 项目初始化脚本

3. **多语言项目**：
   - 混合语言项目的脚本管理
   - 统一的脚本执行界面
   - 参数化配置和复用

### ⚠️ 注意事项

1. 需要在系统中安装对应的语言环境
2. 虚拟环境需要在脚本工作目录中
3. 某些脚本可能需要额外的依赖安装

### 🔄 兼容性

- 向后兼容：现有的 Batch、PowerShell、Shell 脚本不受影响
- 数据库兼容：ScriptType 字段支持新的类型值
- UI 兼容：脚本类型下拉框自动包含新类型

### 📊 统计

- 支持的脚本类型：从 3 种增加到 9 种（+200%）
- 支持的文件扩展名：从 4 个增加到 12 个（+200%）
- 新增代码行数：约 200+ 行
- 新增辅助方法：3 个
