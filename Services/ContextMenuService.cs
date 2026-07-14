using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using ToolBox.Models;
using Serilog;

namespace ToolBox.Services
{
    /// <summary>
    /// 右键菜单注册表管理服务。
    /// </summary>
    public class ContextMenuService
    {
        private const string HKCR_Prefix = "HKEY_CLASSES_ROOT\\";
        private const string HKCU_Prefix = "HKEY_CURRENT_USER\\Software\\Classes\\";

        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, System.Text.StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        /// <summary>
        /// 解析 Windows MUI 间接资源引用字符串（例如将 "@shell32.dll,-8506" 转换成 "在此处打开命令窗口"）。
        /// </summary>
        /// <param name="muiString">可能的间接资源引用字符串。</param>
        /// <returns>解析出的本地化语言字符串；若解析失败则返回原字符串。</returns>
        public static string ExtractMuiString(string muiString)
        {
            // 分支注释：若为空或不以 @ 开头，说明不是 Windows 本地化资源字符串，直接返回
            if (string.IsNullOrWhiteSpace(muiString) || !muiString.StartsWith("@"))
            {
                return muiString;
            }

            try
            {
                var sb = new System.Text.StringBuilder(512);
                int result = SHLoadIndirectString(muiString, sb, sb.Capacity, IntPtr.Zero);
                // 分支注释：若解析成功（返回值 S_OK 即为 0），则使用解析出的文本作为名字
                if (result == 0)
                {
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "解析 MUI 资源间接引用字符串失败: {MuiString}", muiString);
            }

            return muiString;
        }

        /// <summary>
        /// 判断当前进程是否以管理员身份运行。
        /// </summary>
        /// <returns>如果是管理员，返回 true；否则返回 false。</returns>
        public bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            // 校验是否在内置的管理员角色中
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// 请求 UAC 提权并重新启动当前应用程序。
        /// </summary>
        public void ElevateAndRestart()
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas" // 请求管理员凭据进行提权启动
            };
            try
            {
                Process.Start(processInfo);
                App.Current.Exit();
            }
            catch (Exception ex)
            {
                // 如果用户拒绝了 UAC 弹窗，则记录警告并留在当前程序中
                Log.Warning(ex, "用户拒绝了管理员提权请求。");
            }
        }

        /// <summary>
        /// 获取指定范围的所有右键菜单项。
        /// </summary>
        /// <param name="scope">作用范围（文件、文件夹、背景）。</param>
        /// <returns>右键菜单项的列表。</returns>
        public List<ContextMenuItem> GetMenuItems(ContextMenuScope scope)
        {
            var items = new List<ContextMenuItem>();
            string subPath = GetScopeSubPath(scope);

            // 1. 扫描当前用户环境下的自定义注册表项 (HKCU)，这部分内容始终对当前用户可读写
            ScanRegistryRoot(Registry.CurrentUser, $"Software\\Classes\\{subPath}", scope, items, isCustomDefault: true);
            // 分支注释：扫描当前用户本地注册的动态 Shell 扩展 (如用户级 DLL 扩展)
            ScanShellExtensions(Registry.CurrentUser, $"Software\\Classes\\{subPath}", scope, items);

            // 2. 扫描系统全局注册表项 (HKCR)，只读或根据管理员状态可写
            ScanRegistryRoot(Registry.ClassesRoot, subPath, scope, items, isCustomDefault: false);
            // 分支注释：扫描系统全局注册的动态 Shell 扩展 (如 TortoiseSVN、WinRAR 等)
            ScanShellExtensions(Registry.ClassesRoot, subPath, scope, items);

            return items;
        }

        /// <summary>
        /// 扫描特定注册表根路径下的所有右键菜单键值。
        /// </summary>
        private void ScanRegistryRoot(RegistryKey rootKey, string subPath, ContextMenuScope scope, List<ContextMenuItem> items, bool isCustomDefault)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subPath, writable: false);
                // 分支注释：如果该路径在注册表中不存在，则直接返回
                if (key == null)
                {
                    return;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    // 分支注释：避免重复扫描，若已存在相同作用域的键值（HKCU 会覆盖 HKCR 中的同名子项），则跳过
                    if (items.Exists(x => x.KeyName.Equals(subKeyName, StringComparison.OrdinalIgnoreCase) && x.Scope == scope))
                    {
                        continue;
                    }

                    using var subKey = key.OpenSubKey(subKeyName, writable: false);
                    // 分支注释：如果子键不存在，则跳过
                    if (subKey == null)
                    {
                        continue;
                    }

                    // 获取命令行 command 子键
                    using var commandKey = subKey.OpenSubKey("command", writable: false);
                    string commandValue = commandKey?.GetValue(null)?.ToString() ?? string.Empty;

                    // 分支注释：跳过复杂的系统 Shell 扩展处理器（仅支持可直接执行命令的常规菜单项）
                    if (string.IsNullOrWhiteSpace(commandValue) && subKey.OpenSubKey("shellex") != null)
                    {
                        continue;
                    }

                    // 读取显示名称，优先读取默认值，其次是 MUIVerb，若都为空则以键名显示
                    string displayName = subKey.GetValue(null)?.ToString() ?? string.Empty;
                    // 分支注释：如果默认名称为空，尝试读取 MUIVerb 属性
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = subKey.GetValue("MUIVerb")?.ToString() ?? string.Empty;
                    }
                    // 分支注释：若仍为空，尝试读取 FriendlyTypeName 属性（部分内置菜单使用此键值）
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = subKey.GetValue("FriendlyTypeName")?.ToString() ?? string.Empty;
                    }
                    // 分支注释：如果依然为空，使用键的英文原名
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = subKeyName;
                    }

                    // 分支注释：将可能包含的 Windows 本地化间接资源字符串进行转换
                    displayName = ExtractMuiString(displayName);

                    // 判断是否被禁用：若包含 "LegacyDisable" 键值，则表示被隐藏
                    bool hasLegacyDisable = subKey.GetValue("LegacyDisable") != null;
                    // 判断是否为 ToolBox 自定义项
                    bool isCustom = isCustomDefault || subKeyName.StartsWith("ToolBox_Custom_", StringComparison.OrdinalIgnoreCase);

                    // 评估可写性
                    bool isWritable = false;
                    try
                    {
                        using var writeTest = rootKey.OpenSubKey($"{subPath}\\{subKeyName}", writable: true);
                        // 分支注释：如果成功以写权限打开，代表当前进程拥有对该项的写操作权限
                        if (writeTest != null)
                        {
                            isWritable = true;
                        }
                    }
                    catch
                    {
                        // 捕获异常表示无写入权限，此时 writable 保持为 false
                    }

                    items.Add(new ContextMenuItem
                    {
                        KeyName = subKeyName,
                        DisplayName = displayName,
                        Command = commandValue,
                        RegistryPath = $"{rootKey.Name}\\{subPath}\\{subKeyName}",
                        Scope = scope,
                        IsActive = !hasLegacyDisable,
                        IsCustom = isCustom,
                        IsWritable = isWritable,
                        IconPath = subKey.GetValue("Icon")?.ToString() ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "扫描注册表路径发生异常: {SubPath}", subPath);
            }
        }

        /// <summary>
        /// 开启或禁用指定的右键菜单项。
        /// </summary>
        /// <param name="item">要操作的菜单项。</param>
        /// <param name="enable">为 true 启用，为 false 禁用。</param>
        /// <returns>操作是否成功。</returns>
        public bool ToggleMenuItem(ContextMenuItem item, bool enable)
        {
            try
            {
                // 分支注释：判断属于全局级别 (HKCR/HKLM) 还是用户级别 (HKCU)，并映射到对应的根分支
                var root = item.RegistryPath.StartsWith("HKEY_LOCAL_MACHINE") || item.RegistryPath.StartsWith("HKEY_CLASSES_ROOT")
                    ? Registry.ClassesRoot
                    : Registry.CurrentUser;

                // 截取掉根分区前缀，获得相对路径
                string relativePath = item.RegistryPath.Substring(item.RegistryPath.IndexOf('\\') + 1);

                // 分支注释：判断是否为动态 Shell 扩展 (ContextMenuHandlers) 类型
                if (relativePath.Contains("shellex\\ContextMenuHandlers"))
                {
                    string parentPath = relativePath.Substring(0, relativePath.LastIndexOf('\\'));
                    string keyName = relativePath.Substring(relativePath.LastIndexOf('\\') + 1);

                    // 1. 优先尝试修改其默认的 GUID 键值，通过在 GUID 前加减 '-' 前导符来实现禁用/启用
                    using (var subKey = root.OpenSubKey(relativePath, writable: true))
                    {
                        // 分支注释：若能成功以写权限打开子键，且其默认值存在
                        if (subKey != null)
                        {
                            string defaultValue = subKey.GetValue(null)?.ToString() ?? string.Empty;
                            // 分支注释：若要启用且默认值被禁用了（有 '-' 前缀），则剥离该前缀
                            if (enable && defaultValue.StartsWith("-"))
                            {
                                subKey.SetValue(null, defaultValue.Substring(1));
                                Log.Information("成功通过还原 GUID 默认值启用了动态扩展: {Name}", item.DisplayName);
                                return true;
                            }
                            // 分支注释：若要禁用且默认值是有效 GUID（未被禁用），则在其前面加上 '-' 前缀
                            else if (!enable && !defaultValue.StartsWith("-") && defaultValue.StartsWith("{"))
                            {
                                subKey.SetValue(null, "-" + defaultValue);
                                Log.Information("成功通过在 GUID 默认值前加 '-' 禁用了动态扩展: {Name}", item.DisplayName);
                                return true;
                            }
                        }
                    }

                    // 2. 如果无默认的 GUID 键值（比如通过子键名字来直接注册的），则通过重命名子键本身来完成
                    // 分支注释：若要启用，且当前键名有禁用标识 '-'
                    if (enable && keyName.StartsWith("-"))
                    {
                        string cleanKeyName = keyName.Substring(1);
                        return RenameSubKey(root, parentPath, keyName, cleanKeyName);
                    }
                    // 分支注释：若要禁用，且当前键名无禁用标识 '-'
                    else if (!enable && !keyName.StartsWith("-"))
                    {
                        string disabledKeyName = "-" + keyName;
                        return RenameSubKey(root, parentPath, keyName, disabledKeyName);
                    }

                    return false;
                }

                using var key = root.OpenSubKey(relativePath, writable: true);
                // 分支注释：如果获取的键对象为空，代表没有写权限或已被删除
                if (key == null)
                {
                    Log.Warning("无法以写入权限打开注册表路径: {Path}", item.RegistryPath);
                    return false;
                }

                // 分支注释：如果是启用，则删除隐藏标识 LegacyDisable
                if (enable)
                {
                    if (key.GetValue("LegacyDisable") != null)
                    {
                        key.DeleteValue("LegacyDisable");
                    }
                }
                // 分支注释：如果是禁用，则写入空字符串值的 LegacyDisable 强制隐藏该菜单
                else
                {
                    key.SetValue("LegacyDisable", "", RegistryValueKind.String);
                }

                Log.Information("成功设置菜单项 '{Name}' 的启用状态为: {State}", item.DisplayName, enable);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "修改右键菜单项状态失败: {Path}", item.RegistryPath);
                return false;
            }
        }

        /// <summary>
        /// 添加自定义的右键菜单项。
        /// </summary>
        /// <param name="displayName">显示的菜单文本。</param>
        /// <param name="commandStr">执行命令（例如 notepad.exe "%1"）。</param>
        /// <param name="scope">作用域范围。</param>
        /// <param name="iconPath">可选的图标路径。</param>
        /// <returns>添加是否成功。</returns>
        public bool AddCustomMenuItem(string displayName, string commandStr, ContextMenuScope scope, string? iconPath)
        {
            try
            {
                // 分支注释：如果是管理员写入全局系统范围，否则写入当前用户专用范围
                var root = IsAdmin() ? Registry.ClassesRoot : Registry.CurrentUser;
                string subPath = IsAdmin()
                    ? GetScopeSubPath(scope)
                    : $"Software\\Classes\\{GetScopeSubPath(scope)}";

                string uniqueKeyName = $"ToolBox_Custom_{Guid.NewGuid():N}";
                string fullSubKeyPath = $"{subPath}\\{uniqueKeyName}";

                using var menuKey = root.CreateSubKey(fullSubKeyPath, writable: true);
                // 分支注释：如果创建子项失败，返回 false
                if (menuKey == null)
                {
                    return false;
                }

                // 写入默认键值作为菜单展示文本
                menuKey.SetValue(null, displayName);

                // 分支注释：如果有指定图标路径，则写入 Icon 键值
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    menuKey.SetValue("Icon", iconPath);
                }

                // 创建 command 子键用于定义点击执行的具体指令
                using var commandKey = menuKey.CreateSubKey("command", writable: true);
                // 分支注释：如果创建 command 子项失败，返回 false
                if (commandKey == null)
                {
                    return false;
                }
                commandKey.SetValue(null, commandStr);

                Log.Information("成功创建自定义菜单项 '{Name}' 在注册表: {Path}", displayName, fullSubKeyPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建自定义菜单项发生异常: {Name}", displayName);
                return false;
            }
        }

        /// <summary>
        /// 删除自定义右键菜单项。
        /// </summary>
        /// <param name="item">要删除的自定义项。</param>
        /// <returns>删除是否成功。</returns>
        public bool DeleteCustomMenuItem(ContextMenuItem item)
        {
            // 分支注释：非 ToolBox 自定义添加的菜单项目，一律禁止直接删除，防止误删系统核心关联
            if (!item.IsCustom)
            {
                return false;
            }

            try
            {
                var root = item.RegistryPath.StartsWith("HKEY_CLASSES_ROOT") ? Registry.ClassesRoot : Registry.CurrentUser;
                string relativePath = item.RegistryPath.Substring(item.RegistryPath.IndexOf('\\') + 1);

                // 递归删除该键及其下全部子键结构
                root.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
                Log.Information("成功递归删除注册表项: {Path}", item.RegistryPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除自定义注册表菜单发生异常: {Path}", item.RegistryPath);
                return false;
            }
        }

        /// <summary>
        /// 扫描指定路径下的 Shell 扩展动态右键菜单项。
        /// </summary>
        private void ScanShellExtensions(RegistryKey rootKey, string subPath, ContextMenuScope scope, List<ContextMenuItem> items)
        {
            try
            {
                // 分支注释：拼接出 shellex\ContextMenuHandlers 的注册表子路径
                string shellexPath = $"{subPath}ex\\ContextMenuHandlers";
                if (subPath.Contains("Software\\Classes"))
                {
                    // 分支注释：如果针对 HKCU 下的用户级路径，替换 "shell" 部分为 "shellex\\ContextMenuHandlers"
                    shellexPath = subPath.Replace("shell", "shellex\\ContextMenuHandlers");
                }

                using var key = rootKey.OpenSubKey(shellexPath, writable: false);
                // 分支注释：如果该 Shell 扩展路径不存在，直接返回
                if (key == null)
                {
                    return;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    // 分支注释：若该子键以禁用标记 "-" 开头，去掉它用来做重复性校验
                    string cleanKeyName = subKeyName.StartsWith("-") ? subKeyName.Substring(1) : subKeyName;

                    // 分支注释：避免在 HKCU 与 HKCR 之间重复添加
                    if (items.Exists(x => x.KeyName.Equals(cleanKeyName, StringComparison.OrdinalIgnoreCase) && x.Scope == scope))
                    {
                        continue;
                    }

                    using var subKey = key.OpenSubKey(subKeyName, writable: false);
                    // 分支注释：若子键不存在，跳过
                    if (subKey == null)
                    {
                        continue;
                    }

                    string defaultValue = subKey.GetValue(null)?.ToString() ?? string.Empty;

                    // 分支注释：判断是否被禁用：1. 键名本身以 "-" 开头；2. 或是其默认 GUID 键值以 "-" 开头
                    bool isPathDisabled = subKeyName.StartsWith("-");
                    bool isValueDisabled = defaultValue.StartsWith("-");
                    bool isCurrentlyActive = !isPathDisabled && !isValueDisabled;

                    string displayName = cleanKeyName;

                    // 分支注释：确定关联的 COM Class GUID，以便去 CLSID 中查询其中文描述名称
                    string targetGuid = isValueDisabled ? defaultValue.Substring(1) : defaultValue;
                    if (cleanKeyName.StartsWith("{") && cleanKeyName.EndsWith("}"))
                    {
                        targetGuid = cleanKeyName;
                    }

                    if (!string.IsNullOrWhiteSpace(targetGuid) && targetGuid.StartsWith("{") && targetGuid.EndsWith("}"))
                    {
                        try
                        {
                            using var clsidKey = Registry.ClassesRoot.OpenSubKey($"CLSID\\{targetGuid}", writable: false);
                            string clsidName = clsidKey?.GetValue(null)?.ToString() ?? string.Empty;
                            // 分支注释：如果成功查到对应的 CLSID 名称，则展示更友好的名称
                            if (!string.IsNullOrWhiteSpace(clsidName))
                            {
                                displayName = $"{clsidName} ({cleanKeyName})";
                            }
                        }
                        catch
                        {
                            // 降级回退，捕获到异常时保留原名字
                        }
                    }

                    // 本地化 MUI 资源提取
                    displayName = ExtractMuiString(displayName);

                    // 分支注释：通过 InprocServer32 子键寻找动态 Shell 扩展关联的 DLL 物理路径，供后续异步图标提取使用
                    string dllPath = string.Empty;
                    if (!string.IsNullOrWhiteSpace(targetGuid) && targetGuid.StartsWith("{") && targetGuid.EndsWith("}"))
                    {
                        try
                        {
                            using var inprocKey = Registry.ClassesRoot.OpenSubKey($"CLSID\\{targetGuid}\\InprocServer32", writable: false);
                            dllPath = inprocKey?.GetValue(null)?.ToString() ?? string.Empty;
                        }
                        catch
                        {
                            // 降级处理，获取失败时保持 dllPath 为空
                        }
                    }

                    // 评估可写性
                    bool isWritable = false;
                    try
                    {
                        using var writeTest = rootKey.OpenSubKey($"{shellexPath}\\{subKeyName}", writable: true);
                        // 分支注释：若有写权限，writable 设为 true
                        if (writeTest != null)
                        {
                            isWritable = true;
                        }
                    }
                    catch
                    {
                        // 捕获异常表示只读
                    }

                    items.Add(new ContextMenuItem
                    {
                        KeyName = subKeyName,
                        DisplayName = displayName,
                        Command = "动态 Shell 扩展 (Shell Extension DLL)",
                        RegistryPath = $"{rootKey.Name}\\{shellexPath}\\{subKeyName}",
                        Scope = scope,
                        IsActive = isCurrentlyActive,
                        IsCustom = false, // 系统或第三方自带扩展，不可直接删除
                        IsWritable = isWritable,
                        IconPath = dllPath
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "扫描 Shell 动态扩展时发生异常: {SubPath}", subPath);
            }
        }

        /// <summary>
        /// 递归重命名注册表的子项（在 C# 中需进行复制和删除操作来实现重命名）。
        /// </summary>
        private bool RenameSubKey(RegistryKey root, string parentPath, string oldName, string newName)
        {
            try
            {
                using var parentKey = root.OpenSubKey(parentPath, writable: true);
                // 分支注释：如果父路径打开失败，返回 false
                if (parentKey == null)
                {
                    return false;
                }

                using var oldKey = parentKey.OpenSubKey(oldName, writable: false);
                // 分支注释：如果旧键不存在，返回 false
                if (oldKey == null)
                {
                    return false;
                }

                // 创建包含新名称的目标键值结构
                using var newKey = parentKey.CreateSubKey(newName, writable: true);
                // 分支注释：如果创建新键失败，返回 false
                if (newKey == null)
                {
                    return false;
                }

                // 递归拷贝数据值与子节点
                CopyRegistryKey(oldKey, newKey);

                oldKey.Close();
                // 彻底删除原本的旧项树
                parentKey.DeleteSubKeyTree(oldName, throwOnMissingSubKey: false);
                Log.Information("成功将注册表项从 '{OldName}' 重命名为 '{NewName}'", oldName, newName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重命名注册表键值对失败: {OldName} -> {NewName}", oldName, newName);
                return false;
            }
        }

        /// <summary>
        /// 递归拷贝注册表键下所有的 Values 和 SubKeys。
        /// </summary>
        private void CopyRegistryKey(RegistryKey source, RegistryKey destination)
        {
            // 复制所有的 Value 值
            foreach (var valueName in source.GetValueNames())
            {
                var valueKind = source.GetValueKind(valueName);
                destination.SetValue(valueName, source.GetValue(valueName)!, valueKind);
            }

            // 递归复制下级所有子项
            foreach (var subKeyName in source.GetSubKeyNames())
            {
                using var sourceSubKey = source.OpenSubKey(subKeyName, writable: false);
                using var destSubKey = destination.CreateSubKey(subKeyName, writable: true);
                // 分支注释：如果原和目的子键都不为空，递归执行复制
                if (sourceSubKey != null && destSubKey != null)
                {
                    CopyRegistryKey(sourceSubKey, destSubKey);
                }
            }
        }

        /// <summary>
        /// 将 Scope 映射为注册表的相对根路径。
        /// </summary>
        private string GetScopeSubPath(ContextMenuScope scope)
        {
            // 分支注释：映射三种不同的操作范围到对应的注册表路径
            return scope switch
            {
                ContextMenuScope.File => "*\\shell",
                ContextMenuScope.Directory => "Directory\\shell",
                ContextMenuScope.Background => "Directory\\Background\\shell",
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }
        /// <summary>
        /// 检查当前系统是否启用了 Windows 10 经典右键菜单。
        /// </summary>
        /// <returns>若开启了经典风格返回 true；否则返回 false。</returns>
        public bool IsClassicMenuEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", writable: false);
                // 分支注释：如果能正常打开这个 CLSID 的 InprocServer32 键，代表启用了 Win10 经典右键风格
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 开启或关闭 Windows 11 经典右键菜单风格。
        /// </summary>
        /// <param name="enableClassic">为 true 切换为 Win10 经典样式，为 false 恢复为 Win11 默认折叠样式。</param>
        /// <returns>切换是否成功。</returns>
        public bool SetClassicMenu(bool enableClassic)
        {
            try
            {
                string clsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
                
                // 分支注释：如果开启经典样式，则注册并创建 CLSID 子键将默认值置为空字符串
                if (enableClassic)
                {
                    using var key = Registry.CurrentUser.CreateSubKey($"{clsidPath}\\InprocServer32", writable: true);
                    key?.SetValue(null, "");
                    Log.Information("成功向注册表写入 Windows 10 经典右键菜单设置项。");
                }
                // 分支注释：如果是关闭经典样式（恢复 Win11），则删除对应的 CLSID 项树
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(clsidPath, throwOnMissingSubKey: false);
                    Log.Information("成功从注册表中删除 Windows 10 经典右键菜单设置项，恢复 Win11 默认。");
                }

                // 异步或同步重启 explorer 资源管理器以应用更改
                RestartExplorer();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "配置 Windows 11 右键菜单风格时发生异常");
                return false;
            }
        }

        /// <summary>
        /// 杀死并重启资源管理器 (explorer.exe) 进程以应用注册表修改。
        /// </summary>
        private void RestartExplorer()
        {
            try
            {
                // 分支注释：遍历所有名为 explorer 的进程并强制结束，Windows 会在后台自动重新唤起它们
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    process.Kill();
                    process.WaitForExit(3000); // 至多等待其退出三秒
                }
                Log.Information("已成功向系统发送 explorer 进程重启信号。");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重启 explorer.exe 进程失败");
            }
        }
    }
}
