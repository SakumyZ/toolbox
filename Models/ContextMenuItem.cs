using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace ToolBox.Models
{
    /// <summary>
    /// 右键菜单项的作用范围类型。
    /// </summary>
    public enum ContextMenuScope
    {
        /// <summary>
        /// 全局文件右键
        /// </summary>
        File,

        /// <summary>
        /// 文件夹图标右键
        /// </summary>
        Directory,

        /// <summary>
        /// 文件夹或桌面空白背景处右键
        /// </summary>
        Background
    }

    /// <summary>
    /// 右键菜单项的信息模型（实现了属性通知以支持异步图标绑定加载）。
    /// </summary>
    public class ContextMenuItem : INotifyPropertyChanged
    {
        /// <summary>
        /// 注册表子键的名称（例如 "OpenWithCode"）。
        /// </summary>
        public string KeyName { get; set; } = string.Empty;

        /// <summary>
        /// 菜单项在右键菜单中显示的文本名称。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 菜单项关联的命令行执行指令。
        /// </summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// 注册表的完整绝对路径。
        /// </summary>
        public string RegistryPath { get; set; } = string.Empty;

        /// <summary>
        /// 菜单项的作用范围。
        /// </summary>
        public ContextMenuScope Scope { get; set; }

        /// <summary>
        /// 指示菜单项当前是否已启用（若 LegacyDisable 不存在则为 true）。
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 指示菜单项是否为 ToolBox 自定义创建的（以 ToolBox_Custom_ 开头）。
        /// </summary>
        public bool IsCustom { get; set; }

        /// <summary>
        /// 指示当前进程是否对该项的注册表具有写权限。
        /// </summary>
        public bool IsWritable { get; set; }

        /// <summary>
        /// 菜单项显示的图标路径（若存在）。
        /// </summary>
        public string IconPath { get; set; } = string.Empty;

        /// <summary>
        /// 指示该右键菜单项是否是动态 Shell 扩展 (shellex)。
        /// </summary>
        public bool IsShellExtension => RegistryPath.Contains("shellex\\ContextMenuHandlers");

        private ImageSource? _iconImage;
        /// <summary>
        /// 供 UI 绑定的真实提取到的图标图像源。
        /// </summary>
        public ImageSource? IconImage
        {
            get => _iconImage;
            set
            {
                // 分支注释：只在属性值确实发生变化时才触发通知
                if (_iconImage != value)
                {
                    _iconImage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasIcon));
                }
            }
        }

        /// <summary>
        /// 指示当前项目是否成功提取并绑定了真实的图标。
        /// </summary>
        public bool HasIcon => IconImage != null;

        /// <summary>
        /// 属性变更事件通知。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更事件的方法。
        /// </summary>
        /// <param name="propertyName">发生变更的属性名称。</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
