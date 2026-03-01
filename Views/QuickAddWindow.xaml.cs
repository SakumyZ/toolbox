using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 快速新增片段窗口 - 全局热键呼出
    /// </summary>
    public sealed partial class QuickAddWindow : Window
    {
        private readonly DatabaseService _db = DatabaseService.Instance;
        private readonly List<Tag> _allTags;
        private readonly List<long> _selectedTagIds = new();

        private static readonly string[] CommonLanguages = new[]
        {
            "C#", "JavaScript", "TypeScript", "Python", "Java", "Go", "Rust",
            "C++", "C", "HTML", "CSS", "SCSS", "SQL", "JSON", "XML", "YAML",
            "Bash", "PowerShell", "Markdown", "Dockerfile"
        };

        public QuickAddWindow()
        {
            InitializeComponent();

            _allTags = _db.GetAllTags();

            // 设置窗口大小
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32(550, 600));

            // 居中显示
            CenterWindow();

            ExtendsContentIntoTitleBar = true;

            InitializeControls();
        }

        private void CenterWindow()
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            var x = (workArea.Width - 550) / 2;
            var y = (workArea.Height - 600) / 3;
            this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        private void InitializeControls()
        {
            // 语言下拉
            foreach (var lang in CommonLanguages)
            {
                LanguageInput.Items.Add(lang);
            }

            // 文件夹下拉
            FolderInput.Items.Add(new ComboBoxItem { Content = "根目录", Tag = (long)0 });
            foreach (var folder in _db.GetAllFolders())
            {
                FolderInput.Items.Add(new ComboBoxItem { Content = folder.Name, Tag = folder.Id });
            }
            FolderInput.SelectedIndex = 0;
        }

        private void TitleInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        }

        private void TagInput_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var suggestions = _allTags
                    .Where(t => t.Name.Contains(sender.Text, StringComparison.OrdinalIgnoreCase)
                                && !_selectedTagIds.Contains(t.Id))
                    .Select(t => t.Name)
                    .ToList();
                sender.ItemsSource = suggestions;
            }
        }

        private void TagInput_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var tagName = (args.ChosenSuggestion as string ?? sender.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(tagName)) return;

            var existingTag = _allTags.FirstOrDefault(t =>
                t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

            long tagId;
            if (existingTag != null)
            {
                tagId = existingTag.Id;
            }
            else
            {
                var newTag = new Tag { Name = tagName };
                tagId = _db.InsertTag(newTag);
                newTag.Id = tagId;
                _allTags.Add(newTag);
            }

            if (!_selectedTagIds.Contains(tagId))
            {
                _selectedTagIds.Add(tagId);
                UpdateTagChips();
            }

            sender.Text = "";
        }

        private void UpdateTagChips()
        {
            TagChips.Children.Clear();
            foreach (var tagId in _selectedTagIds)
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == tagId);
                if (tag == null) continue;

                var chip = new Button
                {
                    Padding = new Thickness(8, 2, 8, 2),
                    CornerRadius = new CornerRadius(12),
                    Tag = tagId
                };

                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                content.Children.Add(new TextBlock { Text = tag.Name, FontSize = 12 });
                content.Children.Add(new FontIcon { Glyph = "\xE711", FontSize = 10 });
                chip.Content = content;

                chip.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is long removeId)
                    {
                        _selectedTagIds.Remove(removeId);
                        UpdateTagChips();
                    }
                };

                TagChips.Children.Add(chip);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleInput.Text) || string.IsNullOrWhiteSpace(CodeInput.Text))
            {
                // 简单验证提示
                if (string.IsNullOrWhiteSpace(TitleInput.Text))
                    TitleInput.PlaceholderText = "⚠ 标题不能为空";
                if (string.IsNullOrWhiteSpace(CodeInput.Text))
                    CodeInput.PlaceholderText = "⚠ 代码不能为空";
                return;
            }

            var folderId = FolderInput.SelectedItem is ComboBoxItem cbi && cbi.Tag is long fid ? fid : 0L;

            var snippet = new Snippet
            {
                Title = TitleInput.Text.Trim(),
                Code = CodeInput.Text,
                Language = LanguageInput.Text?.Trim() ?? "",
                Description = DescInput.Text?.Trim() ?? "",
                FolderId = folderId,
                IsFavorite = false
            };

            _db.InsertSnippet(snippet, _selectedTagIds);
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
