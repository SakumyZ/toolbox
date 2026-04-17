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
                ActivePathBox.Text = settings.ActiveSkillsPath;
                InactivePathBox.Text = settings.InactiveSkillsPath;

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

            var filtered = _allSkills.AsEnumerable();
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

        /// <summary>
        /// 保存目录配置。
        /// </summary>
        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            // 如果页面正在刷新，则忽略重复点击。
            if (_isRefreshing)
            {
                return;
            }

            var result = _service.SaveSettings(new SkillManagerSettings
            {
                ActiveSkillsPath = ActivePathBox.Text,
                InactiveSkillsPath = InactivePathBox.Text
            });

            // 如果保存失败，则直接展示错误并停止刷新。
            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            await RefreshDataAsync(result.message);
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

            var categoryBox = new TextBox
            {
                Header = "分类",
                PlaceholderText = "例如：Review / 文档 / Redmine",
                Text = skill.Category == "未分类" ? string.Empty : skill.Category
            };

            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 420
            };
            panel.Children.Add(aliasBox);
            panel.Children.Add(categoryBox);

            var dialog = new ContentDialog
            {
                Title = $"编辑 Skill：{skill.SkillId}",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            // 如果用户取消，则保持现状不做任何保存。
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var result = _service.SaveSkillMetadata(skill.SkillId, aliasBox.Text, categoryBox.Text);

            // 如果保存失败，则直接反馈错误信息。
            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            await RefreshDataAsync(result.message);
        }
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
}