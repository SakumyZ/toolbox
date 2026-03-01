using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 代码片段新增/编辑对话框
    /// </summary>
    public sealed partial class SnippetEditDialog : ContentDialog
    {
        private readonly DatabaseService _db = DatabaseService.Instance;
        private readonly Snippet? _editingSnippet;
        private readonly long _defaultFolderId;
        private readonly List<Tag> _allTags;
        private readonly List<SnippetFolder> _allFolders;

        private static readonly string[] CommonLanguages = new[]
        {
            "C#", "JavaScript", "TypeScript", "Python", "Java", "Go", "Rust",
            "C++", "C", "HTML", "CSS", "SCSS", "SQL", "JSON", "XML", "YAML",
            "Bash", "PowerShell", "Markdown", "Dockerfile", "Kotlin", "Swift",
            "Ruby", "PHP", "Lua", "R", "MATLAB", "Dart"
        };

        /// <summary>
        /// 新增模式
        /// </summary>
        public SnippetEditDialog(long defaultFolderId = 0)
        {
            _defaultFolderId = defaultFolderId;
            _allTags = _db.GetAllTags();
            _allFolders = _db.GetAllFolders();
            InitializeDialog("新增代码片段");
        }

        /// <summary>
        /// 编辑模式
        /// </summary>
        public SnippetEditDialog(Snippet snippet)
        {
            _editingSnippet = snippet;
            _defaultFolderId = snippet.FolderId;
            _allTags = _db.GetAllTags();
            _allFolders = _db.GetAllFolders();
            InitializeDialog("编辑代码片段");
        }

        private void InitializeDialog(string title)
        {
            Title = title;
            PrimaryButtonText = "保存";
            CloseButtonText = "取消";
            DefaultButton = ContentDialogButton.Primary;

            // 构建表单内容
            var panel = new StackPanel { Spacing = 12, MinWidth = 500 };

            // 标题
            var titleBox = new TextBox
            {
                Header = "标题",
                PlaceholderText = "给代码片段起个名字",
                Text = _editingSnippet?.Title ?? "",
                Name = "TitleInput"
            };
            panel.Children.Add(titleBox);

            // 语言选择
            var languageCombo = new ComboBox
            {
                Header = "编程语言",
                PlaceholderText = "选择语言",
                IsEditable = true,
                Width = 200,
                Name = "LanguageInput"
            };
            foreach (var lang in CommonLanguages)
            {
                languageCombo.Items.Add(lang);
            }
            if (!string.IsNullOrEmpty(_editingSnippet?.Language))
            {
                languageCombo.Text = _editingSnippet.Language;
            }
            panel.Children.Add(languageCombo);

            // 代码内容
            var codeBox = new TextBox
            {
                Header = "代码内容",
                PlaceholderText = "粘贴或输入代码...",
                Text = _editingSnippet?.Code ?? "",
                AcceptsReturn = true,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Height = 200,
                Name = "CodeInput"
            };
            // 设置 ScrollViewer 属性以支持滚动
            ScrollViewer.SetVerticalScrollBarVisibility(codeBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(codeBox, ScrollBarVisibility.Auto);
            panel.Children.Add(codeBox);

            // 描述/备注
            var descBox = new TextBox
            {
                Header = "描述/备注 (可选)",
                PlaceholderText = "代码用途、来源、注意事项等...",
                Text = _editingSnippet?.Description ?? "",
                AcceptsReturn = true,
                Height = 80,
                Name = "DescInput"
            };
            panel.Children.Add(descBox);

            // 文件夹选择
            var folderCombo = new ComboBox
            {
                Header = "所属文件夹",
                Width = 200,
                Name = "FolderInput"
            };
            folderCombo.Items.Add(new ComboBoxItem { Content = "根目录", Tag = (long)0 });
            foreach (var folder in _allFolders)
            {
                folderCombo.Items.Add(new ComboBoxItem { Content = folder.Name, Tag = folder.Id });
            }
            // 选中默认文件夹
            for (int i = 0; i < folderCombo.Items.Count; i++)
            {
                if (folderCombo.Items[i] is ComboBoxItem item && item.Tag is long id && id == _defaultFolderId)
                {
                    folderCombo.SelectedIndex = i;
                    break;
                }
            }
            if (folderCombo.SelectedIndex < 0) folderCombo.SelectedIndex = 0;
            panel.Children.Add(folderCombo);

            // 标签输入
            var tagPanel = new StackPanel { Spacing = 4 };
            tagPanel.Children.Add(new TextBlock
            {
                Text = "标签",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            });

            var tagInput = new AutoSuggestBox
            {
                PlaceholderText = "输入标签名，回车添加",
                Name = "TagInput"
            };
            tagInput.QuerySubmitted += TagInput_QuerySubmitted;
            tagInput.TextChanged += TagInput_TextChanged;

            tagPanel.Children.Add(tagInput);

            var tagDisplay = new ItemsRepeater { Name = "TagDisplay" };
            _selectedTagIds.Clear();
            if (_editingSnippet != null)
            {
                var existingTagIds = _db.GetSnippetTagIds(_editingSnippet.Id);
                foreach (var tid in existingTagIds)
                {
                    _selectedTagIds.Add(tid);
                }
            }
            tagPanel.Children.Add(tagDisplay);
            panel.Children.Add(tagPanel);

            // 收藏
            var favoriteSwitch = new ToggleSwitch
            {
                Header = "收藏",
                IsOn = _editingSnippet?.IsFavorite ?? false,
                Name = "FavoriteInput"
            };
            panel.Children.Add(favoriteSwitch);

            Content = panel;

            // 保存控件引用
            _titleBox = titleBox;
            _languageCombo = languageCombo;
            _codeBox = codeBox;
            _descBox = descBox;
            _folderCombo = folderCombo;
            _tagInput = tagInput;
            _tagDisplay = tagDisplay;
            _favoriteSwitch = favoriteSwitch;

            PrimaryButtonClick += OnPrimaryButtonClick;

            UpdateTagDisplay();
        }

        // 控件引用
        private TextBox _titleBox = null!;
        private ComboBox _languageCombo = null!;
        private TextBox _codeBox = null!;
        private TextBox _descBox = null!;
        private ComboBox _folderCombo = null!;
        private AutoSuggestBox _tagInput = null!;
        private ItemsRepeater _tagDisplay = null!;
        private ToggleSwitch _favoriteSwitch = null!;
        private readonly List<long> _selectedTagIds = new();

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

            // 查找或创建标签
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
                UpdateTagDisplay();
            }

            sender.Text = "";
        }

        private void UpdateTagDisplay()
        {
            if (_tagDisplay == null) return;

            // 使用 StackPanel 显示标签芯片
            var wrapPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

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

                var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                chipContent.Children.Add(new TextBlock { Text = tag.Name, FontSize = 12 });
                chipContent.Children.Add(new FontIcon { Glyph = "\xE711", FontSize = 10 });
                chip.Content = chipContent;

                chip.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is long removeId)
                    {
                        _selectedTagIds.Remove(removeId);
                        UpdateTagDisplay();
                    }
                };

                wrapPanel.Children.Add(chip);
            }

            // 替换 ItemsRepeater 的内容（使用父容器）
            if (_tagDisplay.Parent is StackPanel parent)
            {
                var index = parent.Children.IndexOf(_tagDisplay);
                // 移除旧的 wrapPanel（如果之前有的话）
                if (index + 1 < parent.Children.Count && parent.Children[index + 1] is StackPanel oldWrap)
                {
                    parent.Children.RemoveAt(index + 1);
                }
                parent.Children.Insert(index + 1, wrapPanel);
            }
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 验证
            if (string.IsNullOrWhiteSpace(_titleBox.Text))
            {
                _titleBox.Header = "标题 (必填)";
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(_codeBox.Text))
            {
                _codeBox.Header = "代码内容 (必填)";
                args.Cancel = true;
                return;
            }

            var folderId = _folderCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is long fid ? fid : 0L;

            var snippet = _editingSnippet ?? new Snippet();
            snippet.Title = _titleBox.Text.Trim();
            snippet.Code = _codeBox.Text;
            snippet.Language = _languageCombo.Text?.Trim() ?? "";
            snippet.Description = _descBox.Text?.Trim() ?? "";
            snippet.FolderId = folderId;
            snippet.IsFavorite = _favoriteSwitch.IsOn;

            if (_editingSnippet != null)
            {
                _db.UpdateSnippet(snippet, _selectedTagIds);
            }
            else
            {
                _db.InsertSnippet(snippet, _selectedTagIds);
            }
        }
    }
}
