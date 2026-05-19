# 自定义解释器功能测试指南

## 🔧 修复内容

### 问题 1：解释器路径没有被保存 ✅ 已修复
- 数据库字段已正确添加
- INSERT 和 UPDATE 语句已包含 CustomInterpreterPath
- BindScriptParameters 已绑定参数

### 问题 2：运行脚本时没有使用自定义解释器 ✅ 已修复
- `BuildStartInfo` 方法已更新，优先使用自定义解释器
- `BuildTerminalStartInfo` 方法已更新，优先使用自定义解释器
- `BuildCommandParts` 方法已更新，命令预览正确显示

## 📋 测试步骤

### 步骤 1：关闭当前应用

1. 关闭正在运行的 ToolBox（Debug 版本）
2. 确保进程完全退出

### 步骤 2：运行新版本

运行新构建的 Release 版本：
```
bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\ToolBox.exe
```

### 步骤 3：测试自定义解释器

#### 测试 A：使用 uv

1. 打开你的 Python 脚本（Excel2Markdown）
2. 在"自定义解释器路径（可选）"输入框中输入：`uv`
3. 点击"保存脚本"
4. 查看"命令预览"区域，应该显示：
   ```
   uv run "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" ...
   ```
5. 切换到其他脚本，再切换回来
6. 确认"自定义解释器路径"显示：`uv`
7. 点击"运行脚本"，确认使用 uv 执行

#### 测试 B：使用完整路径

1. 在"自定义解释器路径"中输入：
   ```
   C:\Users\housei\AppData\Local\Microsoft\WindowsApps\python.exe
   ```
2. 保存脚本
3. 查看命令预览，应该显示：
   ```
   "C:\Users\housei\AppData\Local\Microsoft\WindowsApps\python.exe" "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" ...
   ```
4. 运行脚本，确认使用指定的 Python

#### 测试 C：留空（自动检测）

1. 清空"自定义解释器路径"
2. 保存脚本
3. 查看命令预览，应该显示自动检测的解释器
4. 运行脚本，确认正常执行

## ✅ 预期结果

### 命令预览

#### 使用 uv
```bash
uv run "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" "C:\Users\housei\Desktop\..." "D:\Projects\..."
```

#### 使用完整路径
```bash
"C:\Users\housei\AppData\Local\Microsoft\WindowsApps\python.exe" "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" "C:\Users\housei\Desktop\..." "D:\Projects\..."
```

#### 留空（自动检测）
```bash
"C:\Users\housei\AppData\Local\Microsoft\WindowsApps\python.exe" "C:\Users\housei\AppData\Local\ToolBox\Scripts\5\excel_to_md.py" "C:\Users\housei\Desktop\..." "D:\Projects\..."
```

### 脚本执行

1. **直接运行**：
   - 输出区域显示脚本执行结果
   - 使用配置的解释器

2. **在新终端运行**：
   - 打开新的 cmd 窗口
   - 使用配置的解释器
   - 可以看到完整的执行过程

## 🔍 验证点

### 1. 数据持久化
- ✅ 保存后切换脚本
- ✅ 切换回来后自定义解释器路径仍然存在
- ✅ 重启应用后自定义解释器路径仍然存在

### 2. 命令预览
- ✅ 配置 `uv` 后显示 `uv run`
- ✅ 配置完整路径后显示完整路径
- ✅ 留空后显示自动检测的解释器
- ✅ 路径包含空格时自动添加引号

### 3. 脚本执行
- ✅ 直接运行使用自定义解释器
- ✅ 新终端运行使用自定义解释器
- ✅ 执行结果正确

### 4. 优先级
- ✅ 自定义解释器 > 自动检测
- ✅ 留空时使用自动检测
- ✅ uv 自动添加 run 子命令

## 🐛 故障排查

### 问题：保存后自定义解释器消失

**检查：**
1. 确认运行的是新构建的 Release 版本
2. 检查数据库是否有 CustomInterpreterPath 列
3. 查看应用日志是否有错误

**解决：**
```sql
-- 手动添加列（如果需要）
ALTER TABLE Scripts ADD COLUMN CustomInterpreterPath TEXT NOT NULL DEFAULT '';
```

### 问题：命令预览没有显示解释器

**检查：**
1. 确认自定义解释器路径已保存
2. 刷新页面或重新选择脚本
3. 查看 BuildCommandParts 方法是否被调用

**解决：**
- 重新构建并运行应用
- 清空自定义解释器，保存，再重新输入

### 问题：运行时没有使用自定义解释器

**检查：**
1. 查看执行输出中的错误信息
2. 确认解释器路径正确
3. 测试解释器是否可以在 cmd 中直接运行

**解决：**
```bash
# 测试解释器
uv --version
python --version
C:\path\to\python.exe --version
```

## 📝 测试清单

- [ ] 关闭旧版本应用
- [ ] 运行新版本（Release）
- [ ] 打开 Python 脚本
- [ ] 输入 `uv` 作为自定义解释器
- [ ] 保存脚本
- [ ] 查看命令预览（应显示 `uv run`）
- [ ] 切换到其他脚本
- [ ] 切换回来（自定义解释器应保留）
- [ ] 运行脚本（应使用 uv）
- [ ] 查看输出（应正常执行）
- [ ] 测试新终端运行
- [ ] 测试完整路径
- [ ] 测试留空（自动检测）

## 🎉 成功标志

当你看到以下情况时，说明功能正常：

1. ✅ 命令预览显示：`uv run "script.py" ...`
2. ✅ 保存后切换脚本，再切换回来，自定义解释器仍然是 `uv`
3. ✅ 运行脚本时使用 uv 执行
4. ✅ 输出区域显示正确的执行结果

## 📞 如果还有问题

如果测试后仍然有问题，请提供：
1. 使用的 ToolBox 版本（Debug 还是 Release）
2. 命令预览的截图
3. 执行输出的截图
4. 自定义解释器路径的值
5. 数据库中的 CustomInterpreterPath 值（如果可以查看）
