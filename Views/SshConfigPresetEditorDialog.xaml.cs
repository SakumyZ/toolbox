using System;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;

namespace ToolBox.Views
{
    /// <summary>
    /// SSH config 预设新增/编辑对话框。
    /// </summary>
    public sealed partial class SshConfigPresetEditorDialog : ContentDialog
    {
        private readonly SshConfigPreset _preset;

        public SshConfigPresetEditorDialog(SshConfigPreset? preset = null)
        {
            InitializeComponent();

            _preset = preset ?? new SshConfigPreset();
            Title = _preset.Id > 0 ? "编辑 SSH 预设" : "新增 SSH 预设";

            NameBox.Text = _preset.Name;
            DescriptionBox.Text = _preset.Description;
            ContentBox.Text = NormalizeLineEndings(_preset.Content);
        }

        public SshConfigPreset Preset => _preset;

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ErrorText.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.Text = "预设名称不能为空";
                args.Cancel = true;
                return;
            }

            _preset.Name = NameBox.Text.Trim();
            _preset.Description = DescriptionBox.Text.Trim();
            _preset.Content = NormalizeLineEndings(ContentBox.Text);

            if (_preset.CreatedAt == default)
            {
                _preset.CreatedAt = DateTime.Now;
            }
        }

        private static string NormalizeLineEndings(string content)
        {
            return content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }
    }
}