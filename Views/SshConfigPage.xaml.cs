using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Models;
using ToolBox.Services;
using Windows.UI;

namespace ToolBox.Views
{
    /// <summary>
    /// SSH config 预设切换页面。
    /// </summary>
    public sealed partial class SshConfigPage : Page
    {
        private readonly SshConfigService _service = new();
        private readonly ObservableCollection<SshConfigPresetViewModel> _presets = new();
        private bool _isLoaded;

        public SshConfigPage()
        {
            InitializeComponent();
            PresetList.ItemsSource = _presets;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            await RefreshDataAsync();
        }

        private System.Threading.Tasks.Task RefreshDataAsync(string? statusMessage = null, long? selectPresetId = null)
        {
            _presets.Clear();
            foreach (var preset in _service.GetAllPresets())
            {
                _presets.Add(new SshConfigPresetViewModel(preset));
            }

            CountText.Text = $"({_presets.Count} 个预设)";
            ConfigPathText.Text = $"当前文件：{_service.ConfigPath}";
            StatusMessage.Text = statusMessage ?? string.Empty;

            if (_presets.Count == 0)
            {
                PreviewTitle.Text = "预设预览";
                PreviewMeta.Text = "暂无预设，可先导入当前 config 或新增预设";
                PreviewContent.Text = string.Empty;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            var target = selectPresetId.HasValue
                ? _presets.FirstOrDefault(x => x.Id == selectPresetId.Value)
                : _presets.FirstOrDefault(x => x.IsActive) ?? _presets.First();

            PresetList.SelectedItem = target;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetList.SelectedItem is SshConfigPresetViewModel vm)
            {
                PreviewTitle.Text = vm.Name;
                PreviewMeta.Text = string.IsNullOrWhiteSpace(vm.Description)
                    ? vm.LastUsedText
                    : $"{vm.Description} | {vm.LastUsedText}";
                PreviewContent.Text = vm.Content;
            }
        }

        private async void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SshConfigPresetEditorDialog
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var result = _service.SavePreset(dialog.Preset);
                await RefreshDataAsync(result.message, result.presetId);
            }
        }

        private async void ImportCurrent_Click(object sender, RoutedEventArgs e)
        {
            var preset = _service.CreatePresetFromCurrentConfig();
            var dialog = new SshConfigPresetEditorDialog(preset)
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var result = _service.SavePreset(dialog.Preset);
                await RefreshDataAsync(result.message, result.presetId);
            }
        }

        private async void EditPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long presetId)
            {
                return;
            }

            var preset = _service.GetPreset(presetId);
            if (preset == null)
            {
                return;
            }

            var dialog = new SshConfigPresetEditorDialog(preset)
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var result = _service.SavePreset(dialog.Preset);
                await RefreshDataAsync(result.message, result.presetId);
            }
        }

        private async void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long presetId)
            {
                return;
            }

            var preset = _service.GetPreset(presetId);
            if (preset == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "删除预设",
                Content = $"确定删除预设“{preset.Name}”吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _service.DeletePreset(presetId);
                await RefreshDataAsync("预设已删除");
            }
        }

        private async void ActivatePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not long presetId)
            {
                return;
            }

            var result = _service.ActivatePreset(presetId);
            await RefreshDataAsync(result.message, presetId);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync("已刷新");
        }
    }

    /// <summary>
    /// SSH config 预设展示模型。
    /// </summary>
    public class SshConfigPresetViewModel
    {
        private static readonly SolidColorBrush ActiveBrushValue = new(Color.FromArgb(255, 16, 137, 62));

        private readonly SshConfigPreset _preset;

        public SshConfigPresetViewModel(SshConfigPreset preset)
        {
            _preset = preset;
        }

        public long Id => _preset.Id;
        public string Name => _preset.Name;
        public string Description => string.IsNullOrWhiteSpace(_preset.Description) ? "未填写说明" : _preset.Description;
        public string Content => _preset.Content;
        public bool IsActive => _preset.IsActive;
        public string LastUsedText => _preset.LastUsedAt.HasValue
            ? $"最后使用：{_preset.LastUsedAt:yyyy-MM-dd HH:mm}"
            : "从未应用";
        public Visibility ActiveVisibility => _preset.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public SolidColorBrush ActiveBrush => ActiveBrushValue;
    }
}