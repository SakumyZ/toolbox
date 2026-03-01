using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// 代码片段管理主页面
    /// </summary>
    public sealed partial class SnippetPage : Page
    {
        private readonly DatabaseService _db = DatabaseService.Instance;
        private readonly ObservableCollection<SnippetViewModel> _snippets = new();
        private readonly ObservableCollection<SnippetFolder> _folders = new();
        private long _selectedFolderId = -1; // -1 = 全部
        private long? _editingSnippetId;
        private bool _isNewSnippet;
        private bool _suppressSelection;

        private static readonly string[] CommonLanguages = new[]
        {
            "C#", "JavaScript", "TypeScript", "Python", "Java", "Go", "Rust",
            "C++", "C", "HTML", "CSS", "SCSS", "SQL", "JSON", "XML", "YAML",
            "Bash", "PowerShell", "Markdown", "Dockerfile", "Kotlin", "Swift",
            "Ruby", "PHP", "Lua", "R", "Dart"
        };

        public SnippetPage()
        {
            InitializeComponent();
            SnippetList.ItemsSource = _snippets;
            FolderList.ItemsSource = _folders;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFolders();
            LoadLanguageFilter();
            PopulateDetailCombos();
            LoadSnippets();
        }

        // ========== 数据加载 ==========

        private void LoadFolders()
        {
            _folders.Clear();
            // 添加虚拟"全部"项
            _folders.Add(new SnippetFolder { Id = -1, Name = "📋 全部片段" });
            foreach (var folder in _db.GetAllFolders())
            {
                _folders.Add(folder);
            }
        }

        private void LoadLanguageFilter()
        {
            LanguageFilter.Items.Clear();
            LanguageFilter.Items.Add("全部语言");
            foreach (var lang in _db.GetDistinctLanguages())
            {
                LanguageFilter.Items.Add(lang);
            }
            LanguageFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// 初始化详情面板的下拉选项
        /// </summary>
        private void PopulateDetailCombos()
        {
            DetailLanguage.Items.Clear();
            foreach (var lang in CommonLanguages)
            {
                DetailLanguage.Items.Add(lang);
            }
            RefreshDetailFolders();
        }

        private void RefreshDetailFolders()
        {
            DetailFolder.Items.Clear();
            DetailFolder.Items.Add(new ComboBoxItem { Content = "根目录", Tag = 0L });
            foreach (var folder in _db.GetAllFolders())
            {
                DetailFolder.Items.Add(new ComboBoxItem { Content = folder.Name, Tag = folder.Id });
            }
        }

        private void LoadSnippets()
        {
            var previousId = _editingSnippetId;

            _suppressSelection = true;
            _snippets.Clear();

            long? folderId = _selectedFolderId >= 0 ? _selectedFolderId : null;
            string? searchText = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text;
            string? language = LanguageFilter.SelectedIndex > 0 ? LanguageFilter.SelectedItem as string : null;
            bool? isFavorite = FavoriteFilter.IsChecked == true ? true : null;

            var snippets = _db.GetSnippets(folderId, searchText, language, isFavorite: isFavorite);

            foreach (var s in snippets)
            {
                _snippets.Add(new SnippetViewModel(s));
            }

            _suppressSelection = false;

            // 更新计数和空状态
            CountText.Text = $"({_snippets.Count} 个)";
            EmptyState.Visibility = _snippets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SnippetList.Visibility = _snippets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // 尝试重新选中之前的项
            if (!_isNewSnippet && previousId.HasValue)
            {
                var vm = _snippets.FirstOrDefault(s => s.Id == previousId.Value);
                if (vm != null)
                {
                    SnippetList.SelectedItem = vm;
                }
                else
                {
                    ClearDetail();
                }
            }
        }

        // ========== 文件夹操作 ==========

        private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderList.SelectedItem is SnippetFolder folder)
            {
                _selectedFolderId = folder.Id;
                LoadSnippets();
            }
        }

        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var input = new TextBox { PlaceholderText = "文件夹名称" };
            var dialog = new ContentDialog
            {
                Title = "新建文件夹",
                Content = input,
                PrimaryButtonText = "创建",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
                !string.IsNullOrWhiteSpace(input.Text))
            {
                _db.InsertFolder(new SnippetFolder { Name = input.Text.Trim() });
                LoadFolders();
                RefreshDetailFolders();
            }
        }

        private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long folderId)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除文件夹",
                    Content = "确定删除这个文件夹吗？文件夹内的片段将移到根目录。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _db.DeleteFolder(folderId);
                    _selectedFolderId = -1;
                    LoadFolders();
                    RefreshDetailFolders();
                    LoadSnippets();
                }
            }
        }

        // ========== 详情面板 ==========

        /// <summary>
        /// 通过 ID 选中列表中的片段
        /// </summary>
        private void SelectSnippetById(long snippetId)
        {
            var vm = _snippets.FirstOrDefault(s => s.Id == snippetId);
            if (vm != null)
            {
                SnippetList.SelectedItem = vm;
            }
        }

        private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;

            if (SnippetList.SelectedItem is SnippetViewModel vm)
            {
                ShowDetail(vm.Model);
            }
            else if (!_isNewSnippet)
            {
                ClearDetail();
            }
        }

        /// <summary>
        /// 显示已有片段的详情（可编辑）
        /// </summary>
        private void ShowDetail(Snippet snippet)
        {
            _editingSnippetId = snippet.Id;
            _isNewSnippet = false;

            DetailTitle.Text = snippet.Title;
            DetailLanguage.Text = snippet.Language ?? "";
            DetailDesc.Text = snippet.Description;
            DetailCode.Text = snippet.Code;
            DetailTags.Text = snippet.Tags;

            SelectDetailFolder(snippet.FolderId);
            UpdateFavIcon(snippet.IsFavorite);

            DetailPanel.Visibility = Visibility.Visible;
            DetailEmpty.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 显示新增片段的空白表单
        /// </summary>
        private void ShowNewDetail()
        {
            _isNewSnippet = true;
            _editingSnippetId = null;

            _suppressSelection = true;
            SnippetList.SelectedItem = null;
            _suppressSelection = false;

            DetailTitle.Text = "";
            DetailLanguage.Text = "";
            DetailDesc.Text = "";
            DetailCode.Text = "";
            DetailTags.Text = "";

            var targetFolderId = _selectedFolderId >= 0 ? _selectedFolderId : 0;
            SelectDetailFolder(targetFolderId);
            UpdateFavIcon(false);

            DetailPanel.Visibility = Visibility.Visible;
            DetailEmpty.Visibility = Visibility.Collapsed;

            DetailTitle.Focus(FocusState.Programmatic);
        }

        private void ClearDetail()
        {
            _editingSnippetId = null;
            _isNewSnippet = false;
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailEmpty.Visibility = Visibility.Visible;
        }

        private void SelectDetailFolder(long folderId)
        {
            for (int i = 0; i < DetailFolder.Items.Count; i++)
            {
                if (DetailFolder.Items[i] is ComboBoxItem item && item.Tag is long id && id == folderId)
                {
                    DetailFolder.SelectedIndex = i;
                    return;
                }
            }
            DetailFolder.SelectedIndex = 0;
        }

        private void UpdateFavIcon(bool isFavorite)
        {
            DetailFavIcon.Glyph = isFavorite ? "\xE735" : "\xE734";
            DetailFavIcon.Foreground = isFavorite
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gold)
                : null;
        }

        // ========== 操作按钮 ==========

        private void AddSnippet_Click(object sender, RoutedEventArgs e)
        {
            ShowNewDetail();
        }

        private void SaveDetail_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DetailTitle.Text))
            {
                DetailTitle.PlaceholderText = "⚠ 标题不能为空";
                DetailTitle.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrWhiteSpace(DetailCode.Text))
            {
                DetailCode.PlaceholderText = "⚠ 代码不能为空";
                DetailCode.Focus(FocusState.Programmatic);
                return;
            }

            var folderId = DetailFolder.SelectedItem is ComboBoxItem cbi && cbi.Tag is long fid ? fid : 0L;
            var tagIds = ParseAndSyncTags(DetailTags.Text);
            bool isFavorite = DetailFavIcon.Glyph == "\xE735";

            if (_isNewSnippet)
            {
                var snippet = new Snippet
                {
                    Title = DetailTitle.Text.Trim(),
                    Code = DetailCode.Text,
                    Language = DetailLanguage.Text?.Trim() ?? "",
                    Description = DetailDesc.Text?.Trim() ?? "",
                    FolderId = folderId,
                    IsFavorite = isFavorite
                };
                var newId = _db.InsertSnippet(snippet, tagIds);
                _editingSnippetId = newId;
                _isNewSnippet = false;

                LoadSnippets();
                LoadLanguageFilter();
                LoadFolders();
                RefreshDetailFolders();
                SelectSnippetById(newId);
            }
            else if (_editingSnippetId.HasValue)
            {
                var existing = _db.GetSnippets().FirstOrDefault(s => s.Id == _editingSnippetId.Value);
                if (existing == null) return;

                existing.Title = DetailTitle.Text.Trim();
                existing.Code = DetailCode.Text;
                existing.Language = DetailLanguage.Text?.Trim() ?? "";
                existing.Description = DetailDesc.Text?.Trim() ?? "";
                existing.FolderId = folderId;
                existing.IsFavorite = isFavorite;

                _db.UpdateSnippet(existing, tagIds);
                var id = _editingSnippetId.Value;
                LoadSnippets();
                LoadLanguageFilter();
                SelectSnippetById(id);
            }

            CountText.Text = "✓ 已保存";
        }

        private List<long> ParseAndSyncTags(string tagsText)
        {
            var tagIds = new List<long>();
            if (string.IsNullOrWhiteSpace(tagsText)) return tagIds;

            var tagNames = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in tagNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var tagId = _db.InsertTag(new Tag { Name = name.Trim() });
                tagIds.Add(tagId);
            }
            return tagIds;
        }

        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            bool currentFav = DetailFavIcon.Glyph == "\xE735";
            UpdateFavIcon(!currentFav);

            if (!_isNewSnippet && _editingSnippetId.HasValue)
            {
                _db.ToggleFavorite(_editingSnippetId.Value);
                var id = _editingSnippetId.Value;
                LoadSnippets();
                SelectSnippetById(id);
            }
        }

        private async void DeleteCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_isNewSnippet)
            {
                ClearDetail();
                return;
            }

            if (!_editingSnippetId.HasValue) return;

            var dialog = new ContentDialog
            {
                Title = "删除片段",
                Content = "确定删除这个代码片段吗？此操作不可恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _db.DeleteSnippet(_editingSnippetId.Value);
                ClearDetail();
                LoadSnippets();
            }
        }

        private void SnippetList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (SnippetList.SelectedItem is SnippetViewModel vm)
            {
                CopyToClipboard(vm.Model);
            }
        }

        private void CopySnippet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long snippetId)
            {
                SelectSnippetById(snippetId);

                if (!string.IsNullOrEmpty(DetailCode.Text))
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(DetailCode.Text);
                    Clipboard.SetContent(dataPackage);
                    _db.IncrementUsageCount(snippetId);
                    CountText.Text = $"已复制: {DetailTitle.Text}";
                }
            }
        }

        private async void DeleteSnippet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long snippetId)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除片段",
                    Content = "确定删除这个代码片段吗？此操作不可恢复。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _db.DeleteSnippet(snippetId);
                    if (_editingSnippetId == snippetId)
                    {
                        ClearDetail();
                    }
                    LoadSnippets();
                }
            }
        }

        private void CopyPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(DetailCode.Text))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(DetailCode.Text);
                Clipboard.SetContent(dataPackage);
                if (_editingSnippetId.HasValue)
                {
                    _db.IncrementUsageCount(_editingSnippetId.Value);
                }
                CountText.Text = $"已复制: {DetailTitle.Text}";
            }
        }

        private void CopyToClipboard(Snippet snippet)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(snippet.Code);
            Clipboard.SetContent(dataPackage);
            _db.IncrementUsageCount(snippet.Id);
            CountText.Text = $"已复制: {snippet.Title}";
        }

        // ========== 搜索/筛选 ==========

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            LoadSnippets();
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                LoadSnippets();
            }
        }

        private void LanguageFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 仅在页面已加载时响应
            if (SnippetList != null)
            {
                LoadSnippets();
            }
        }

        private void FavoriteFilter_Click(object sender, RoutedEventArgs e)
        {
            LoadSnippets();
        }
    }

    /// <summary>
    /// 片段列表的 ViewModel，处理显示逻辑
    /// </summary>
    public class SnippetViewModel
    {
        public Snippet Model { get; }

        public long Id => Model.Id;
        public string Title => Model.Title;
        public string Language => Model.Language;
        public string Tags => Model.Tags;

        /// <summary>
        /// 语言标签显示/隐藏
        /// </summary>
        public Visibility LanguageVisibility =>
            string.IsNullOrEmpty(Model.Language) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// 代码预览（前 2 行）
        /// </summary>
        public string CodePreview
        {
            get
            {
                var lines = Model.Code.Split('\n');
                var preview = string.Join("\n", lines.Take(2)).Trim();
                return preview.Length > 120 ? preview[..120] + "..." : preview;
            }
        }

        /// <summary>
        /// 收藏状态 → Visibility 转换
        /// </summary>
        public Visibility IsFavorite => Model.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        public SnippetViewModel(Snippet model)
        {
            Model = model;
        }
    }
}
