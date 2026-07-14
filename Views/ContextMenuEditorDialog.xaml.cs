using System;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;

namespace ToolBox.Views
{
    /// <summary>
    /// 自定义右键菜单项添加/编辑对话框。
    /// </summary>
    public sealed partial class ContextMenuEditorDialog : ContentDialog
    {
        /// <summary>
        /// 录入的菜单显示名称。
        /// </summary>
        public string MenuName { get; private set; } = string.Empty;

        /// <summary>
        /// 录入的命令行内容。
        /// </summary>
        public string MenuCommand { get; private set; } = string.Empty;

        /// <summary>
        /// 选择的作用作用范围。
        /// </summary>
        public ContextMenuScope MenuScope { get; private set; }

        /// <summary>
        /// 录入的图标路径（可选）。
        /// </summary>
        public string? MenuIcon { get; private set; }

        /// <summary>
        /// 初始化对话框实例。
        /// </summary>
        public ContextMenuEditorDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 点击创建确认按钮时的处理程序。
        /// </summary>
        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 分支注释：如果用户未填写核心的显示名称或者命令行，则阻止关闭对话框
            if (string.IsNullOrWhiteSpace(MenuNameInput.Text) || string.IsNullOrWhiteSpace(MenuCommandInput.Text))
            {
                args.Cancel = true;
                return;
            }

            MenuName = MenuNameInput.Text.Trim();
            MenuCommand = MenuCommandInput.Text.Trim();
            MenuIcon = string.IsNullOrWhiteSpace(MenuIconInput.Text) ? null : MenuIconInput.Text.Trim();

            // 分支注释：将下拉列表的选中索引转换成对应的右键菜单作用域枚举
            MenuScope = MenuScopeInput.SelectedIndex switch
            {
                0 => ContextMenuScope.File,
                1 => ContextMenuScope.Directory,
                2 => ContextMenuScope.Background,
                _ => ContextMenuScope.File
            };
        }
    }
}
