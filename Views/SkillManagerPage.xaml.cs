using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.Views
{
    /// <summary>
    /// Skill 切换管理页面。
    /// </summary>
    public sealed partial class SkillManagerPage : Page
    {
        private readonly SkillManagerService _service = new();
        private readonly ObservableCollection<SkillItemViewModel> _displaySkills = new();
        private List<SkillItem> _allSkills = new();
        private readonly Dictionary<string, SkillItemViewModel> _viewModelMap = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded;
        private bool _isRefreshing;
        private bool _isApplyingToggle;

        public ObservableCollection<AgentDirItem> AgentDirsCollection { get; } = new();

        public SkillManagerPage()
        {
            InitializeComponent();
            SkillList.ItemsSource = _displaySkills;
        }

        /// <summary>
        /// 页面首次加载时初始化数据。
        /// </summary>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 如果页面已经初始化，则避免重复加载。
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            await RefreshDataAsync(showLoadingState: true);
        }

        /// <summary>
        /// 刷新页面数据。
        /// </summary>
        private async Task RefreshDataAsync(string? statusMessage = null, bool showLoadingState = false)
        {
            // 如果已经在刷新，则避免重复触发造成界面抖动。
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;

            // 如果需要显式加载态，则显示进度圈并临时隐藏列表。
            if (showLoadingState)
            {
                LoadingRing.IsActive = true;
                SkillList.Visibility = Visibility.Collapsed;
            }

            try
            {
                var settings = _service.GetSettings();

                _allSkills = await Task.Run(() => _service.GetAllSkills());
                LoadCategoryFilter();
                ApplyFilter();
                StatusMessage.Text = statusMessage ?? string.Empty;
            }
            finally
            {
                // 如果显示过加载态，则在结束时恢复列表显示。
                if (showLoadingState)
                {
                    LoadingRing.IsActive = false;
                    SkillList.Visibility = Visibility.Visible;
                }

                _isRefreshing = false;
            }
        }

        /// <summary>
        /// 根据当前筛选条件刷新列表。
        /// </summary>
        private void ApplyFilter()
        {
            _displaySkills.Clear();

            var filtered = _allSkills.Where(item => !item.IsArchived).AsEnumerable();
            var searchText = SearchBox.Text?.Trim();

            // 如果用户输入了关键词，则按名称、别名和描述做模糊筛选。
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(item =>
                    item.SkillId.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    item.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            // 如果选中了具体分类，则仅显示对应分类。
            if (CategoryFilter.SelectedItem is string category && category != "全部分类")
            {
                filtered = filtered.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            // 如果选中了状态筛选，则按启停状态过滤。
            if (StateFilter.SelectedItem is ComboBoxItem stateItem && stateItem.Tag is string stateTag)
            {
                filtered = stateTag switch
                {
                    "Active" => filtered.Where(item => item.IsActive),
                    "Inactive" => filtered.Where(item => !item.IsActive),
                    _ => filtered
                };
            }

            foreach (var item in filtered)
            {
                _displaySkills.Add(GetOrCreateViewModel(item));
            }

            UpdateCountText();
        }

        /// <summary>
        /// 更新单个 skill 的本地状态，避免整页刷新造成闪动。
        /// </summary>
        private void UpdateSkillStateLocally(SkillItem skill, bool isActive)
        {
            var settings = _service.GetSettings();
            skill.IsActive = isActive;
            skill.CurrentPath = Path.Combine(isActive ? settings.ActiveSkillsPath : settings.InactiveSkillsPath, skill.SkillId);

            if (_viewModelMap.TryGetValue(skill.SkillId, out var viewModel))
            {
                viewModel.NotifyStateChanged();

                // 如果当前状态筛选与新状态不匹配，则从当前显示列表中移除这一项。
                if (!MatchesCurrentStateFilter(skill.IsActive))
                {
                    _displaySkills.Remove(viewModel);
                }
            }

            UpdateCountText();
        }

        /// <summary>
        /// 获取当前项对应的稳定 ViewModel。
        /// </summary>
        private SkillItemViewModel GetOrCreateViewModel(SkillItem skill)
        {
            if (_viewModelMap.TryGetValue(skill.SkillId, out var existingViewModel))
            {
                existingViewModel.UpdateModel(skill);
                return existingViewModel;
            }

            var viewModel = new SkillItemViewModel(skill);
            _viewModelMap[skill.SkillId] = viewModel;
            return viewModel;
        }

        /// <summary>
        /// 判断某个状态是否符合当前状态筛选器。
        /// </summary>
        private bool MatchesCurrentStateFilter(bool isActive)
        {
            if (StateFilter.SelectedItem is not ComboBoxItem stateItem || stateItem.Tag is not string stateTag)
            {
                return true;
            }

            return stateTag switch
            {
                "Active" => isActive,
                "Inactive" => !isActive,
                _ => true
            };
        }

        /// <summary>
        /// 更新顶部统计文本。
        /// </summary>
        private void UpdateCountText()
        {
            var activeCount = _allSkills.Count(item => item.IsActive);
            var inactiveCount = _allSkills.Count - activeCount;
            CountText.Text = $"(共 {_displaySkills.Count} 个，启用 {activeCount}，停用 {inactiveCount})";
        }

        /// <summary>
        /// 重新加载分类筛选项。
        /// </summary>
        private void LoadCategoryFilter()
        {
            var categories = _service.GetAllCategories();
            var currentSelection = CategoryFilter.SelectedItem as string ?? "全部分类";

            CategoryFilter.Items.Clear();
            foreach (var category in categories)
            {
                CategoryFilter.Items.Add(category);
            }

            // 如果之前的分类仍然存在，则保留用户选择。
            if (categories.Contains(currentSelection, StringComparer.OrdinalIgnoreCase))
            {
                CategoryFilter.SelectedItem = categories.First(item => string.Equals(item, currentSelection, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                CategoryFilter.SelectedIndex = 0;
            }
        }

        private async void AdvancedSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _service.GetSettings();
            DialogActivePathBox.Text = settings.ActiveSkillsPath;
            DialogInactivePathBox.Text = settings.InactiveSkillsPath;
            
            AgentDirsCollection.Clear();
            if (settings.AgentDirectories != null)
            {
                foreach (var dir in settings.AgentDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        AgentDirsCollection.Add(new AgentDirItem { Path = dir });
                    }
                }
            }

            await AdvancedSettingsDialog.ShowAsync();
        }

        private async void CategoryManager_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCategoryListAsync();
            await CategoryManagerDialog.ShowAsync();
        }

        private async Task RefreshCategoryListAsync()
        {
            var rawCategories = await Task.Run(() => _service.GetAllCategoriesForManagement());
            var viewModels = new ObservableCollection<CategoryItemViewModel>();
            foreach (var cat in rawCategories)
            {
                Microsoft.UI.Xaml.Media.Brush brush;
                if (string.IsNullOrWhiteSpace(cat.ColorHex))
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentFillColorSecondaryBrush", out var res) && res is Microsoft.UI.Xaml.Media.Brush b)
                    {
                        brush = b;
                    }
                    else
                    {
                        brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    }
                }
                else
                {
                    try { brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColorHex(cat.ColorHex)); }
                    catch { brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); }
                }
                viewModels.Add(new CategoryItemViewModel { CategoryName = cat.Category, BackgroundBrush = brush });
            }
            CategoryList.ItemsSource = viewModels;
            EmptyCategoryText.Visibility = viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RenameCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string oldName)
            {
                var textBox = new TextBox { Text = oldName, PlaceholderText = "新分类名称", MinWidth = 300 };
                var renameDialog = new ContentDialog
                {
                    Title = "重命名分类",
                    Content = textBox,
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };
                
                if (await renameDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var newName = textBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        StatusMessage.Text = "分类名不能为空";
                        return;
                    }
                    var result = await Task.Run(() => _service.RenameCategory(oldName, newName));
                    StatusMessage.Text = result.message;
                    if (result.success)
                    {
                        await RefreshDataAsync();
                        await RefreshCategoryListAsync();
                    }
                }
            }
        }

        private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string categoryName)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "彻底删除",
                    Content = $"确定要删除分类 '{categoryName}' 吗？相关 Skill 将被置为未分类状态。",
                    PrimaryButtonText = "确认删除",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };

                if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var result = await Task.Run(() => _service.DeleteCategory(categoryName));
                    StatusMessage.Text = result.message;
                    if (result.success)
                    {
                        await RefreshDataAsync();
                        await RefreshCategoryListAsync();
                    }
                }
            }
        }

        private async void ArchiveManager_Click(object sender, RoutedEventArgs e)
        {
            await RefreshArchiveListAsync();
            await ArchiveManagerDialog.ShowAsync();
        }

        private async Task RefreshArchiveListAsync()
        {
            var archivedSkills = await Task.Run(() => _service.GetAllSkills().Where(s => s.IsArchived).ToList());
            ArchivedSkillList.ItemsSource = archivedSkills;
            EmptyArchiveText.Visibility = archivedSkills.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void UnarchiveSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string skillId)
            {
                var result = _service.ArchiveSkill(skillId, false);
                StatusMessage.Text = result.message;
                if (result.success)
                {
                    await RefreshDataAsync();
                    await RefreshArchiveListAsync();
                }
            }
        }

        private async void DeleteArchivedSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string skillId)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "彻底删除",
                    Content = $"确定要彻底删除已归档的 Skill '{skillId}' 及其全部文件吗？此操作不可恢复。",
                    PrimaryButtonText = "确认删除",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };

                if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var result = await Task.Run(() => _service.DeleteSkill(skillId));
                    StatusMessage.Text = result.message;
                    if (result.success)
                    {
                        await RefreshDataAsync();
                        await RefreshArchiveListAsync();
                    }
                }
            }
        }

        private async void AdvancedSettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var agentDirs = AgentDirsCollection
                .Select(item => item.Path?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var result = _service.SaveSettings(new SkillManagerSettings
            {
                ActiveSkillsPath = DialogActivePathBox.Text,
                InactiveSkillsPath = DialogInactivePathBox.Text,
                AgentDirectories = agentDirs!
            });

            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            await RefreshDataAsync(result.message);
        }

        private async void BrowseActivePath_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                DialogActivePathBox.Text = folder;
            }
        }

        private async void BrowseInactivePath_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                DialogInactivePathBox.Text = folder;
            }
        }

        private async void BrowseAgentDir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AgentDirItem item)
            {
                var folder = await PickFolderAsync();
                if (folder != null)
                {
                    item.Path = folder;
                }
            }
        }

        private void AddAgentDir_Click(object sender, RoutedEventArgs e)
        {
            AgentDirsCollection.Add(new AgentDirItem { Path = string.Empty });
        }

        private void RemoveAgentDir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AgentDirItem item)
            {
                AgentDirsCollection.Remove(item);
            }
        }

        private async Task<string?> PickFolderAsync()
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");
            
            var window = App.MainWindowInstance;
            if (window != null)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            }
            
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        /// <summary>
        /// 手动刷新列表。
        /// </summary>
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // 如果页面正在刷新，则忽略重复点击。
            if (_isRefreshing)
            {
                return;
            }

            await RefreshDataAsync("已刷新");
        }

        /// <summary>
        /// 搜索文本变化时更新列表。
        /// </summary>
        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // 如果不是用户输入触发，则不需要重新筛选。
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            ApplyFilter();
        }

        /// <summary>
        /// 筛选项变更时更新列表。
        /// </summary>
        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            // 如果页面仍在刷新，则跳过中间态事件。
            if (_isRefreshing)
            {
                return;
            }

            ApplyFilter();
        }

        /// <summary>
        /// 切换 skill 启停状态。
        /// </summary>
        private async void SkillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // 如果列表正在刷新，则忽略重复操作。
            if (_isRefreshing || _isApplyingToggle)
            {
                return;
            }

            if (sender is not ToggleSwitch toggle || toggle.Tag is not string skillId)
            {
                return;
            }

            var targetState = toggle.IsOn;
            var current = _allSkills.FirstOrDefault(item => string.Equals(item.SkillId, skillId, StringComparison.OrdinalIgnoreCase));

            // 如果没有找到目标项，则直接返回。
            if (current == null)
            {
                return;
            }

            // 如果当前状态与目标状态一致，则无需执行移动操作。
            if (current.IsActive == targetState)
            {
                return;
            }

            _isApplyingToggle = true;
            toggle.IsEnabled = false;

            try
            {
                var result = await Task.Run(() => _service.SetSkillActive(skillId, targetState));

                // 如果切换失败，则把控件状态回滚到真实值。
                if (!result.success)
                {
                    toggle.IsOn = current.IsActive;
                    StatusMessage.Text = result.message;
                    return;
                }

                UpdateSkillStateLocally(current, targetState);
                StatusMessage.Text = result.message;
            }
            finally
            {
                toggle.IsEnabled = true;
                _isApplyingToggle = false;
            }
        }

        /// <summary>
        /// 编辑 skill 的别名与分类。
        /// </summary>
        private async void EditSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string skillId)
            {
                return;
            }

            var skill = _allSkills.FirstOrDefault(item => string.Equals(item.SkillId, skillId, StringComparison.OrdinalIgnoreCase));

            // 如果没有找到目标 skill，则直接返回。
            if (skill == null)
            {
                return;
            }

            var aliasBox = new TextBox
            {
                Header = "别名",
                PlaceholderText = "为空则直接显示文件夹名",
                Text = skill.Alias
            };

            var categoryBox = new ComboBox
            {
                Header = "分类",
                PlaceholderText = "可选择或输入新分类（清空为未分类）",
                IsEditable = true,
                Text = skill.Category == "未分类" ? string.Empty : skill.Category,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var categories = _service.GetAllCategories();
            foreach (var cat in categories)
            {
                if (cat != "全部分类" && cat != "未分类")
                {
                    categoryBox.Items.Add(cat);
                }
            }

            var colorPicker = new Microsoft.UI.Xaml.Controls.ColorPicker
            {
                IsMoreButtonVisible = false,
                IsColorSliderVisible = true,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                IsAlphaEnabled = false,
                IsAlphaSliderVisible = false,
                IsAlphaTextInputVisible = false
            };

            var colorRect = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), BorderThickness = new Thickness(1) };
            
            var presetColorsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
            string[] presets = { "#FF5733", "#33FF57", "#3357FF", "#F333FF", "#FFB533", "#33FFF5" };
            bool isColorModified = false;
            foreach (var hex in presets)
            {
                var btn = new Button 
                { 
                    Background = GetBrushFromHex(hex), 
                    Width = 32, 
                    Height = 32, 
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0)
                };
                btn.Click += (s, ev) => 
                {
                    try
                    {
                        colorPicker.Color = ParseColorHex(hex);
                        isColorModified = true;
                    }
                    catch { }
                };
                presetColorsPanel.Children.Add(btn);
            }

            var flyoutStack = new StackPanel { Spacing = 8 };
            flyoutStack.Children.Add(new TextBlock { Text = "预设颜色", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
            flyoutStack.Children.Add(presetColorsPanel);
            flyoutStack.Children.Add(colorPicker);

            var colorFlyout = new Flyout { Content = flyoutStack };

            var colorBtnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            colorBtnContent.Children.Add(colorRect);
            colorBtnContent.Children.Add(new TextBlock { Text = "选择分类颜色" });

            var colorButton = new Button
            {
                Content = colorBtnContent,
                Flyout = colorFlyout,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 0, 0)
            };

            var categoryGrid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
            Grid.SetColumn(categoryBox, 0);
            Grid.SetColumn(colorButton, 1);
            categoryGrid.Children.Add(categoryBox);
            categoryGrid.Children.Add(colorButton);

            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 420
            };
            panel.Children.Add(aliasBox);
            panel.Children.Add(categoryGrid);

            string currentColorHex = _service.GetCategoryColor(skill.Category);
            if (!string.IsNullOrWhiteSpace(currentColorHex))
            {
                try
                {
                    var col = ParseColorHex(currentColorHex);
                    colorPicker.Color = col;
                    colorRect.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(col);
                }
                catch { }
            }

            colorPicker.ColorChanged += (s, ev) =>
            {
                isColorModified = true;
                colorRect.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ev.NewColor);
            };

            categoryBox.SelectionChanged += (s, ev) =>
            {
                if (categoryBox.SelectedItem is string selectedText)
                {
                    UpdateColorPickerForCategory(selectedText);
                }
            };

            void UpdateColorPickerForCategory(string categoryText)
            {
                var text = categoryText?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var hex = _service.GetCategoryColor(text);
                    if (!string.IsNullOrWhiteSpace(hex))
                    {
                        try
                        {
                            var col = ParseColorHex(hex);
                            colorPicker.Color = col;
                            colorRect.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(col);
                            isColorModified = true;
                        }
                        catch { }
                    }
                }
            }

            var dialog = new ContentDialog
            {
                Title = $"编辑 Skill：{skill.SkillId}",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var actionButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 24, 0, 0) };
            
            var archiveBtn = new Button { Content = "归档", Padding = new Thickness(12, 4, 12, 4) };
            archiveBtn.Click += async (s, ev) =>
            {
                dialog.Hide();
                var result = _service.ArchiveSkill(skillId, true);
                StatusMessage.Text = result.message;
                if (result.success)
                {
                    await RefreshDataAsync();
                }
            };

            var deleteBtn = new Button { Content = "删除", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(0) };
            deleteBtn.Resources.Add("ButtonBackground", new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColorHex("#D13438")));
            deleteBtn.Resources.Add("ButtonBackgroundPointerOver", new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColorHex("#FF99A4")));
            deleteBtn.Resources.Add("ButtonBackgroundPressed", new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColorHex("#C62828")));
            deleteBtn.Click += async (s, ev) =>
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "彻底删除",
                    Content = $"确定要彻底删除 Skill '{skillId}' 及其全部文件吗？此操作不可恢复。",
                    PrimaryButtonText = "确认删除",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };
                if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    dialog.Hide();
                    var result = await Task.Run(() => _service.DeleteSkill(skillId));
                    StatusMessage.Text = result.message;
                    if (result.success)
                    {
                        await RefreshDataAsync();
                    }
                }
            };
            
            actionButtonsPanel.Children.Add(archiveBtn);
            actionButtonsPanel.Children.Add(deleteBtn);
            panel.Children.Add(actionButtonsPanel);


            // 如果用户取消，则保持现状不做任何保存。
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            string newColorHex = "";
            if (isColorModified || (!string.IsNullOrWhiteSpace(currentColorHex) && string.Equals(categoryBox.Text.Trim(), skill.Category, StringComparison.OrdinalIgnoreCase)))
            {
                newColorHex = $"#{colorPicker.Color.R:X2}{colorPicker.Color.G:X2}{colorPicker.Color.B:X2}";
            }
            
            var result = _service.SaveSkillMetadata(skill.SkillId, aliasBox.Text, categoryBox.Text, newColorHex);

            // 如果保存失败，则直接反馈错误信息。
            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            await RefreshDataAsync(result.message);
        }
        private Windows.UI.Color ParseColorHex(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255;
            byte r = 0, g = 0, b = 0;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }

        private Microsoft.UI.Xaml.Media.Brush GetBrushFromHex(string hex)
        {
            try
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColorHex(hex));
            }
            catch
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
    }

    public class CategoryItemViewModel
    {
        public string CategoryName { get; set; } = "";
        public Microsoft.UI.Xaml.Media.Brush BackgroundBrush { get; set; } = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>
    /// Skill 列表项视图模型。
    /// </summary>
    public class SkillItemViewModel : INotifyPropertyChanged
    {
        private SkillItem _skill;

        public SkillItemViewModel(SkillItem skill)
        {
            _skill = skill;
        }

        /// <summary>
        /// 用最新的模型对象替换当前引用。
        /// </summary>
        public void UpdateModel(SkillItem skill)
        {
            _skill = skill;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CategoryBackgroundBrush));
        }

        /// <summary>
        /// Skill 原始标识。
        /// </summary>
        public string SkillId => _skill.SkillId;

        /// <summary>
        /// 展示名称。
        /// </summary>
        public string DisplayName => _skill.DisplayName;

        /// <summary>
        /// Skill 分类。
        /// </summary>
        public string Category => _skill.Category;

        /// <summary>
        /// Skill 分类颜色背景刷。
        /// </summary>
        public Microsoft.UI.Xaml.Media.Brush CategoryBackgroundBrush
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_skill.CategoryColorHex))
                {
                    try
                    {
                        var hex = _skill.CategoryColorHex.TrimStart('#');
                        byte a = 255;
                        byte r = 0, g = 0, b = 0;
                        if (hex.Length == 8)
                        {
                            a = Convert.ToByte(hex.Substring(0, 2), 16);
                            r = Convert.ToByte(hex.Substring(2, 2), 16);
                            g = Convert.ToByte(hex.Substring(4, 2), 16);
                            b = Convert.ToByte(hex.Substring(6, 2), 16);
                        }
                        else if (hex.Length == 6)
                        {
                            r = Convert.ToByte(hex.Substring(0, 2), 16);
                            g = Convert.ToByte(hex.Substring(2, 2), 16);
                            b = Convert.ToByte(hex.Substring(4, 2), 16);
                        }
                        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
                    }
                    catch { }
                }
                
                if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentFillColorSecondaryBrush", out var res) && res is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    return brush;
                }
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        /// <summary>
        /// 是否已启用。
        /// </summary>
        public bool IsActive => _skill.IsActive;

        /// <summary>
        /// 属性变更通知事件。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 通知界面当前状态已变化。
        /// </summary>
        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsActive));
        }

        /// <summary>
        /// 触发属性变更通知。
        /// </summary>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    public class AgentDirItem : INotifyPropertyChanged
    {
        private string _path = string.Empty;
        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}