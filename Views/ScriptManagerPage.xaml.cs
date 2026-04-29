using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ToolBox.Models;
using ToolBox.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ToolBox.Views
{
    /// <summary>
    /// 脚本管理页面。
    /// </summary>
    public sealed partial class ScriptManagerPage : Page
    {
        private readonly ScriptManagerService _service = new();
        private readonly ObservableCollection<ScriptListItemViewModel> _scripts = new();
        private readonly ObservableCollection<ScriptParameterItemViewModel> _parameterItems = new();
        private readonly Dictionary<long, FrameworkElement> _parameterInputMap = new();

        private List<ScriptDefinition> _allScripts = new();
        private List<ScriptParameterDefinition> _editingParameters = new();
        private long? _editingScriptId;
        private string? _pendingImportPath;
        private bool _isLoaded;
        private bool _isCreatingNew;
        private bool _isRefreshing;
        private long _nextTemporaryParameterId = -1;

        public ScriptManagerPage()
        {
            InitializeComponent();
            ScriptList.ItemsSource = _scripts;
            ParameterList.ItemsSource = _parameterItems;

            foreach (var scriptType in ScriptTypes.All)
            {
                ScriptTypeBox.Items.Add(scriptType);
            }

            UpdateTopActionButtons();
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

        private System.Threading.Tasks.Task RefreshDataAsync(string? statusMessage = null, long? selectScriptId = null)
        {
            _isRefreshing = true;

            _allScripts = _service.GetAllScripts();
            ApplyScriptFilter();
            CountText.Text = $"({_allScripts.Count} 个脚本)";
            StatusMessage.Text = statusMessage ?? string.Empty;

            var targetId = selectScriptId ?? _editingScriptId;
            if (targetId.HasValue)
            {
                var target = _allScripts.FirstOrDefault(item => item.Id == targetId.Value);
                if (target != null)
                {
                    ScriptList.SelectedItem = _scripts.FirstOrDefault(item => item.Id == target.Id);
                }
            }

            _isRefreshing = false;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void ApplyScriptFilter()
        {
            _scripts.Clear();
            var searchText = SearchBox.Text?.Trim();

            var filtered = _allScripts.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(item =>
                    item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    item.FileName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var script in filtered)
            {
                _scripts.Add(new ScriptListItemViewModel(script));
            }
        }

        private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            if (ScriptList.SelectedItem is not ScriptListItemViewModel vm)
            {
                return;
            }

            LoadScript(vm.Script);
        }

        private void LoadScript(ScriptDefinition script)
        {
            _editingScriptId = script.Id;
            _isCreatingNew = false;
            _pendingImportPath = null;
            _nextTemporaryParameterId = -1;

            NameBox.Text = script.Name;
            DescriptionBox.Text = script.Description;
            ScriptTypeBox.SelectedItem = script.ScriptType;
            ScriptPathBox.Text = _service.GetScriptAbsolutePath(script);
            WorkingDirectoryBox.Text = script.WorkingDirectory;
            OutputBox.Text = string.Empty;
            OpenInTerminalCheckBox.IsChecked = script.IsRunInTerminal;

            _editingParameters = script.Parameters
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(CloneParameter)
                .ToList();

            RefreshParameterList();
            RenderRunForm();
            UpdateFolderButtonState();
            UpdateCommandPreview();
            UpdateTopActionButtons();
        }

        private void AddScript_Click(object sender, RoutedEventArgs e)
        {
            _isCreatingNew = true;
            _editingScriptId = null;
            _pendingImportPath = null;
            _nextTemporaryParameterId = -1;

            ScriptList.SelectedItem = null;
            NameBox.Text = string.Empty;
            DescriptionBox.Text = string.Empty;
            ScriptTypeBox.SelectedItem = ScriptTypes.Batch;
            ScriptPathBox.Text = string.Empty;
            WorkingDirectoryBox.Text = string.Empty;
            OutputBox.Text = string.Empty;
            OpenInTerminalCheckBox.IsChecked = false;

            _editingParameters = new List<ScriptParameterDefinition>();
            RefreshParameterList();
            RenderRunForm();
            UpdateFolderButtonState();
            UpdateCommandPreview();
            UpdateTopActionButtons();
            StatusMessage.Text = "已切换到新建脚本";
        }

        private async void BrowseScript_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".cmd");
            picker.FileTypeFilter.Add(".ps1");
            picker.FileTypeFilter.Add(".sh");

            var window = App.MainWindowInstance;
            if (window != null)
            {
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            }

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            if (!_service.IsSupportedScriptFile(file.Path))
            {
                StatusMessage.Text = "仅支持导入 .bat、.ps1、.sh 脚本";
                return;
            }

            _pendingImportPath = file.Path;
            ScriptPathBox.Text = file.Path;
            ScriptTypeBox.SelectedItem = ResolveScriptType(file.Path);

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(file.Name);
            }

            StatusMessage.Text = "脚本文件已选择，保存后会导入到 ToolBox 管理目录";
            UpdateFolderButtonState();
            UpdateCommandPreview();
        }

        private async void SaveScript_Click(object sender, RoutedEventArgs e)
        {
            var script = BuildScriptFromForm();
            if (script == null)
            {
                return;
            }

            script.Parameters = _editingParameters
                .Select(CloneParameter)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .ToList();

            var result = _service.SaveScript(script, _pendingImportPath);
            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            _editingScriptId = result.scriptId;
            _isCreatingNew = false;
            _pendingImportPath = null;
            await RefreshDataAsync(result.message, result.scriptId);

            var saved = _service.GetScript(result.scriptId);
            if (saved != null)
            {
                LoadScript(saved);
            }

            UpdateTopActionButtons();
        }

        private void OpenScriptFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!_editingScriptId.HasValue)
            {
                StatusMessage.Text = "请先保存脚本后再打开目录";
                return;
            }

            var script = _service.GetScript(_editingScriptId.Value);
            if (script == null)
            {
                StatusMessage.Text = "未找到当前脚本";
                return;
            }

            var scriptDirectory = _service.GetScriptDirectory(script);
            if (string.IsNullOrWhiteSpace(scriptDirectory) || !Directory.Exists(scriptDirectory))
            {
                StatusMessage.Text = "脚本目录不存在";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{scriptDirectory}\"",
                UseShellExecute = true
            });
            StatusMessage.Text = "已打开脚本目录";
        }

        private async void DeleteScript_Click(object sender, RoutedEventArgs e)
        {
            if (!_editingScriptId.HasValue)
            {
                StatusMessage.Text = "当前没有可删除的脚本";
                return;
            }

            var script = _service.GetScript(_editingScriptId.Value);
            if (script == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "删除脚本",
                Content = $"确定删除脚本“{script.Name}”吗？脚本文件和参数配置都会一并删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _service.DeleteScript(script.Id);
            AddScript_Click(sender, e);
            await RefreshDataAsync("脚本已删除");
            UpdateTopActionButtons();
        }

        private async void DuplicateScript_Click(object sender, RoutedEventArgs e)
        {
            if (!_editingScriptId.HasValue)
            {
                StatusMessage.Text = "请先选择一个脚本";
                return;
            }

            var result = _service.DuplicateScript(_editingScriptId.Value);
            if (!result.success)
            {
                StatusMessage.Text = result.message;
                return;
            }

            await RefreshDataAsync(result.message, result.scriptId);
            var duplicated = _service.GetScript(result.scriptId);
            if (duplicated != null)
            {
                LoadScript(duplicated);
            }
        }

        private async void RunScript_Click(object sender, RoutedEventArgs e)
        {
            if (!_editingScriptId.HasValue)
            {
                StatusMessage.Text = "请先保存脚本后再运行";
                return;
            }

            var script = _service.GetScript(_editingScriptId.Value);
            if (script == null)
            {
                StatusMessage.Text = "未找到当前脚本";
                return;
            }

            var parameterValues = CollectParameterValues();

            if (OpenInTerminalCheckBox.IsChecked == true)
            {
                var terminalResult = _service.StartScriptInTerminal(script, parameterValues);
                StatusMessage.Text = terminalResult.message;
                if (terminalResult.success)
                {
                    OutputBox.Text = $"[{DateTime.Now:HH:mm:ss}] 已在新终端中启动 {script.Name}。";
                }
                return;
            }

            OutputBox.Text = $"[{DateTime.Now:HH:mm:ss}] 正在执行 {script.Name}...{Environment.NewLine}";

            var result = await _service.ExecuteScriptAsync(
                script,
                parameterValues,
                line => DispatcherQueue.TryEnqueue(() =>
                {
                    OutputBox.Text += line + Environment.NewLine;
                }),
                line => DispatcherQueue.TryEnqueue(() =>
                {
                    OutputBox.Text += line + Environment.NewLine;
                }));
            OutputBox.Text = BuildExecutionOutput(script, result);
            StatusMessage.Text = result.Message;
        }

        private void CopyCommandPreview_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CommandPreviewBox.Text))
            {
                StatusMessage.Text = "当前没有可复制的命令";
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(CommandPreviewBox.Text);
            Clipboard.SetContent(dataPackage);
            StatusMessage.Text = "命令预览已复制";
        }

        private async void AddParameter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ScriptParameterEditorDialog
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var parameter = CloneParameter(dialog.Parameter);
            parameter.ScriptId = _editingScriptId ?? 0;
            if (parameter.Id <= 0)
            {
                parameter.Id = _nextTemporaryParameterId--;
            }
            if (parameter.SortOrder <= 0)
            {
                parameter.SortOrder = _editingParameters.Count + 1;
            }

            _editingParameters.Add(parameter);
            NormalizeParameterSortOrder();
            RefreshParameterList();
            RenderRunForm();
            UpdateCommandPreview();
        }

        private async void EditParameter_Click(object sender, RoutedEventArgs e)
        {
            await EditSelectedParameterAsync();
        }

        private async void ParameterList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            await EditSelectedParameterAsync();
        }

        private async System.Threading.Tasks.Task EditSelectedParameterAsync()
        {
            if (ParameterList.SelectedItem is not ScriptParameterItemViewModel item)
            {
                StatusMessage.Text = "请先选择一个参数";
                return;
            }

            var parameter = _editingParameters.FirstOrDefault(x => ReferenceEquals(x, item.Parameter) || IsSameParameter(x, item.Parameter));
            if (parameter == null)
            {
                return;
            }

            var dialog = new ScriptParameterEditorDialog(parameter)
            {
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var updated = dialog.Parameter;
            parameter.Name = updated.Name;
            parameter.DisplayName = updated.DisplayName;
            parameter.ControlType = updated.ControlType;
            parameter.ArgumentName = updated.ArgumentName;
            parameter.DefaultValue = updated.DefaultValue;
            parameter.Placeholder = updated.Placeholder;
            parameter.HelpText = updated.HelpText;
            parameter.IsRequired = updated.IsRequired;
            parameter.SortOrder = updated.SortOrder;

            NormalizeParameterSortOrder();
            RefreshParameterList();
            RenderRunForm();
            UpdateCommandPreview();
        }

        private void DeleteParameter_Click(object sender, RoutedEventArgs e)
        {
            if (ParameterList.SelectedItem is not ScriptParameterItemViewModel item)
            {
                StatusMessage.Text = "请先选择一个参数";
                return;
            }

            var target = _editingParameters.FirstOrDefault(x => ReferenceEquals(x, item.Parameter) || IsSameParameter(x, item.Parameter));
            if (target == null)
            {
                return;
            }

            _editingParameters.Remove(target);
            NormalizeParameterSortOrder();
            RefreshParameterList();
            RenderRunForm();
            UpdateCommandPreview();
        }

        private void RefreshParameterList()
        {
            _parameterItems.Clear();

            foreach (var parameter in _editingParameters.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
            {
                _parameterItems.Add(new ScriptParameterItemViewModel(parameter));
            }

            ParameterHintText.Text = _parameterItems.Count == 0
                ? "尚未配置参数"
                : $"共 {_parameterItems.Count} 个参数";
        }

        private void RenderRunForm()
        {
            _parameterInputMap.Clear();
            RunParameterHost.Children.Clear();

            if (_editingParameters.Count == 0)
            {
                RunParameterHost.Children.Add(new TextBlock
                {
                    Text = "这个脚本暂时不需要参数，可以直接运行。",
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                return;
            }

            foreach (var parameter in _editingParameters.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
            {
                var label = ResolveDisplayName(parameter);
                FrameworkElement inputControl;

                if (IsBooleanControlType(parameter.ControlType))
                {
                    var toggle = new ToggleSwitch
                    {
                        Header = label,
                        IsOn = string.Equals(parameter.DefaultValue, "true", StringComparison.OrdinalIgnoreCase),
                        OnContent = "开启",
                        OffContent = "关闭"
                    };
                    toggle.Toggled += RunParameterValueChanged;
                    inputControl = toggle;
                }
                else if (parameter.ControlType == ScriptParameterControlTypes.Multiline)
                {
                    var textBox = new TextBox
                    {
                        Header = label,
                        Text = parameter.DefaultValue,
                        PlaceholderText = parameter.Placeholder,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        MinHeight = 88
                    };
                    textBox.TextChanged += RunParameterValueChanged;
                    inputControl = textBox;
                }
                else
                {
                    var textBox = new TextBox
                    {
                        Header = label,
                        Text = parameter.DefaultValue,
                        PlaceholderText = parameter.Placeholder
                    };
                    textBox.TextChanged += RunParameterValueChanged;
                    inputControl = textBox;
                }

                RunParameterHost.Children.Add(inputControl);
                if (!string.IsNullOrWhiteSpace(parameter.HelpText))
                {
                    RunParameterHost.Children.Add(new TextBlock
                    {
                        Text = parameter.HelpText,
                        FontSize = 12,
                        Margin = new Thickness(2, -4, 0, 0),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    });
                }

                _parameterInputMap[parameter.Id] = inputControl;
            }
        }

        private void RunParameterValueChanged(object sender, object e)
        {
            UpdateCommandPreview();
        }

        private Dictionary<long, string> CollectParameterValues()
        {
            var values = new Dictionary<long, string>();

            foreach (var parameter in _editingParameters)
            {
                _parameterInputMap.TryGetValue(parameter.Id, out var element);

                string value;
                if (element is ToggleSwitch toggle)
                {
                    value = toggle.IsOn ? "true" : "false";
                }
                else if (element is TextBox textBox)
                {
                    value = textBox.Text;
                }
                else
                {
                    value = parameter.DefaultValue;
                }

                values[parameter.Id] = value;
            }

            return values;
        }

        private ScriptDefinition? BuildScriptFromForm()
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                StatusMessage.Text = "脚本名称不能为空";
                NameBox.Focus(FocusState.Programmatic);
                return null;
            }

            if (_editingScriptId == null && string.IsNullOrWhiteSpace(_pendingImportPath))
            {
                StatusMessage.Text = "请先导入一个脚本文件";
                return null;
            }

            if (ScriptTypeBox.SelectedItem is not string scriptType)
            {
                scriptType = ScriptTypes.Batch;
            }

            var existing = _editingScriptId.HasValue
                ? _service.GetScript(_editingScriptId.Value) ?? new ScriptDefinition()
                : new ScriptDefinition();

            existing.Name = NameBox.Text.Trim();
            existing.Description = DescriptionBox.Text.Trim();
            existing.WorkingDirectory = WorkingDirectoryBox.Text.Trim();
            existing.ScriptType = scriptType;
            existing.IsRunInTerminal = OpenInTerminalCheckBox.IsChecked == true;
            return existing;
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            ApplyScriptFilter();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync("已刷新", _editingScriptId);
            UpdateCommandPreview();
        }

        private void NormalizeParameterSortOrder()
        {
            var ordered = _editingParameters
                .OrderBy(item => item.SortOrder <= 0 ? int.MaxValue : item.SortOrder)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int index = 0; index < ordered.Count; index++)
            {
                ordered[index].SortOrder = index + 1;
            }

            _editingParameters = ordered;
        }

        private void UpdateFolderButtonState()
        {
            if (OpenScriptFolderButton == null)
            {
                return;
            }

            if (!_editingScriptId.HasValue)
            {
                OpenScriptFolderButton.IsEnabled = false;
                return;
            }

            var script = _service.GetScript(_editingScriptId.Value);
            if (script == null)
            {
                OpenScriptFolderButton.IsEnabled = false;
                return;
            }

            var scriptDirectory = _service.GetScriptDirectory(script);
            OpenScriptFolderButton.IsEnabled =
                !string.IsNullOrWhiteSpace(scriptDirectory) && Directory.Exists(scriptDirectory);
        }

        private void UpdateTopActionButtons()
        {
            if (TopSaveButton == null || TopRunButton == null || TopDuplicateButton == null || TopDeleteButton == null)
            {
                return;
            }

            var hasImportedOrSavedScript = _editingScriptId.HasValue || !string.IsNullOrWhiteSpace(_pendingImportPath);
            var hasSavedScript = _editingScriptId.HasValue;

            TopSaveButton.IsEnabled = true;
            TopRunButton.IsEnabled = hasSavedScript;
            TopDuplicateButton.IsEnabled = hasSavedScript;
            TopDeleteButton.IsEnabled = hasSavedScript;

            if (!hasImportedOrSavedScript)
            {
                TopRunButton.IsEnabled = false;
            }
        }

        private void UpdateCommandPreview()
        {
            if (CommandPreviewBox == null || CopyCommandButton == null)
            {
                return;
            }

            var script = BuildPreviewScript();
            var scriptPath = ResolvePreviewScriptPath();
            if (script == null || string.IsNullOrWhiteSpace(scriptPath))
            {
                CommandPreviewBox.Text = "先导入并保存脚本，随后这里会显示可复制的完整命令。";
                CopyCommandButton.IsEnabled = false;
                return;
            }

            var parameterValues = CollectParameterValues();
            var commandPreview = _service.BuildCommandPreview(script, scriptPath, parameterValues);
            CommandPreviewBox.Text = string.IsNullOrWhiteSpace(commandPreview)
                ? "当前脚本还无法生成命令预览。"
                : commandPreview;
            CopyCommandButton.IsEnabled = !string.IsNullOrWhiteSpace(commandPreview);
        }

        private ScriptDefinition? BuildPreviewScript()
        {
            if (ScriptTypeBox.SelectedItem is not string scriptType)
            {
                return null;
            }

            return new ScriptDefinition
            {
                Id = _editingScriptId ?? 0,
                Name = NameBox.Text.Trim(),
                Description = DescriptionBox.Text.Trim(),
                ScriptType = scriptType,
                WorkingDirectory = WorkingDirectoryBox.Text.Trim(),
                Parameters = _editingParameters.Select(CloneParameter).ToList()
            };
        }

        private string ResolvePreviewScriptPath()
        {
            if (!string.IsNullOrWhiteSpace(_pendingImportPath))
            {
                return _pendingImportPath!;
            }

            if (!_editingScriptId.HasValue)
            {
                return string.Empty;
            }

            var script = _service.GetScript(_editingScriptId.Value);
            return script == null ? string.Empty : _service.GetScriptAbsolutePath(script);
        }

        private static ScriptParameterDefinition CloneParameter(ScriptParameterDefinition source)
        {
            return new ScriptParameterDefinition
            {
                Id = source.Id,
                ScriptId = source.ScriptId,
                Name = source.Name,
                DisplayName = source.DisplayName,
                ControlType = source.ControlType,
                ArgumentName = source.ArgumentName,
                DefaultValue = source.DefaultValue,
                Placeholder = source.Placeholder,
                HelpText = source.HelpText,
                IsRequired = source.IsRequired,
                SortOrder = source.SortOrder
            };
        }

        private static bool IsSameParameter(ScriptParameterDefinition left, ScriptParameterDefinition right)
        {
            if (left.Id > 0 && right.Id > 0)
            {
                return left.Id == right.Id;
            }

            return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) &&
                   left.SortOrder == right.SortOrder;
        }

        private static string ResolveDisplayName(ScriptParameterDefinition parameter)
        {
            return string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName;
        }

        private static string ResolveScriptType(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return extension switch
            {
                ".ps1" => ScriptTypes.PowerShell,
                ".sh" => ScriptTypes.Shell,
                _ => ScriptTypes.Batch
            };
        }

private static bool IsBooleanControlType(string? controlType)
    {
        return string.Equals(controlType, ScriptParameterControlTypes.Boolean, StringComparison.OrdinalIgnoreCase);
    }

        private static string BuildExecutionOutput(ScriptDefinition script, ScriptExecutionResult result)
        {
            return
                $"[{result.StartedAt:yyyy-MM-dd HH:mm:ss}] 开始执行：{script.Name}{Environment.NewLine}" +
                $"[{result.FinishedAt:yyyy-MM-dd HH:mm:ss}] {result.Message}{Environment.NewLine}" +
                $"ExitCode: {result.ExitCode}{Environment.NewLine}" +
                $"{Environment.NewLine}--- STDOUT ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(result.StandardOutput) ? "(empty)" : result.StandardOutput)}{Environment.NewLine}" +
                $"{Environment.NewLine}--- STDERR ---{Environment.NewLine}" +
                $"{(string.IsNullOrWhiteSpace(result.StandardError) ? "(empty)" : result.StandardError)}";
        }
    }

    /// <summary>
    /// 脚本列表展示模型。
    /// </summary>
    public class ScriptListItemViewModel
    {
        public ScriptListItemViewModel(ScriptDefinition script)
        {
            Script = script;
        }

        public ScriptDefinition Script { get; }
        public long Id => Script.Id;
        public string Name => Script.Name;
        public string Description => string.IsNullOrWhiteSpace(Script.Description) ? "未填写说明" : Script.Description;
        public string ScriptType => Script.ScriptType;
        public string FileName => string.IsNullOrWhiteSpace(Script.FileName) ? "未导入脚本文件" : Script.FileName;
        public string ParameterSummary => $"参数 {Script.Parameters.Count} 个";
    }

    /// <summary>
    /// 参数列表展示模型。
    /// </summary>
    public class ScriptParameterItemViewModel
    {
        public ScriptParameterItemViewModel(ScriptParameterDefinition parameter)
        {
            Parameter = parameter;
        }

        public ScriptParameterDefinition Parameter { get; }
        public string DisplayName => string.IsNullOrWhiteSpace(Parameter.DisplayName) ? Parameter.Name : Parameter.DisplayName;
        public string ControlType => Parameter.ControlType;
        public string RequirementText => Parameter.IsRequired ? "必填" : "可选";
        public string SecondaryText => string.IsNullOrWhiteSpace(Parameter.ArgumentName)
            ? $"内部名：{Parameter.Name}"
            : $"{Parameter.ArgumentName} | {Parameter.Name}";
    }
}
