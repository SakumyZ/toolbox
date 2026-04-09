using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// SSH config 预设新增/编辑对话框。
    /// </summary>
    public sealed class SshConfigPresetDialog : ContentDialog
    {
        private readonly SshConfigService _service = new();
        private readonly SshConfigPreset _preset;

        private TextBox _nameBox = null!;
        private TextBox _descriptionBox = null!;
        private TextBox _contentBox = null!;
        private TextBlock _errorText = null!;

        public SshConfigPresetDialog(SshConfigPreset? preset = null)
        {
            _preset = preset ?? new SshConfigPreset();
            InitializeDialog();
        }

        public SshConfigPreset Preset => _preset;

        private void InitializeDialog()
        {
            Title = _preset.Id > 0 ? "编辑 SSH 预设" : "新增 SSH 预设";
            PrimaryButtonText = "保存";
            CloseButtonText = "取消";
            DefaultButton = ContentDialogButton.Primary;
            PrimaryButtonClick += OnPrimaryButtonClick;

            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 700
            };

            _nameBox = new TextBox
            {
                Header = "预设名称",
                PlaceholderText = "例如：公司 GitHub / 个人 GitHub",
                Text = _preset.Name
            };
            panel.Children.Add(_nameBox);

            _descriptionBox = new TextBox
            {
                Header = "说明",
                PlaceholderText = "可选，描述这个预设的用途",
                Text = _preset.Description
            };
            panel.Children.Add(_descriptionBox);

            _contentBox = new TextBox
            {
                Header = "SSH Config 内容",
                PlaceholderText = "在这里粘贴整份 SSH config",
                Text = NormalizeLineEndings(_preset.Content),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Height = 360
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_contentBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(_contentBox, ScrollBarVisibility.Auto);
            panel.Children.Add(_contentBox);

            _errorText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_errorText);

            Content = panel;
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            _errorText.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                _errorText.Text = "预设名称不能为空";
                args.Cancel = true;
                return;
            }

            _preset.Name = _nameBox.Text.Trim();
            _preset.Description = _descriptionBox.Text.Trim();
            _preset.Content = NormalizeLineEndings(_contentBox.Text);
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