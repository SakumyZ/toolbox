using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;

namespace ToolBox.Views
{
    /// <summary>
    /// 脚本参数编辑对话框。
    /// </summary>
    public sealed class ScriptParameterEditorDialog : ContentDialog
    {
        private readonly ScriptParameterDefinition _parameter;

        private TextBox _nameBox = null!;
        private TextBox _displayNameBox = null!;
        private ComboBox _controlTypeCombo = null!;
        private TextBox _argumentNameBox = null!;
        private TextBox _placeholderBox = null!;
        private TextBox _defaultTextBox = null!;
        private ToggleSwitch _defaultToggle = null!;
        private TextBox _helpTextBox = null!;
        private ToggleSwitch _requiredToggle = null!;
        private NumberBox _sortOrderBox = null!;
        private TextBlock _errorText = null!;

        public ScriptParameterEditorDialog(ScriptParameterDefinition? parameter = null)
        {
            _parameter = parameter == null
                ? new ScriptParameterDefinition()
                : Clone(parameter);
            InitializeDialog();
        }

        public ScriptParameterDefinition Parameter => _parameter;

        private void InitializeDialog()
        {
            Title = _parameter.Id > 0 ? "编辑参数" : "新增参数";
            PrimaryButtonText = "保存";
            CloseButtonText = "取消";
            DefaultButton = ContentDialogButton.Primary;
            PrimaryButtonClick += OnPrimaryButtonClick;

            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 480
            };

            _nameBox = new TextBox
            {
                Header = "参数名称",
                PlaceholderText = "例如：input"
            };
            panel.Children.Add(_nameBox);

            _displayNameBox = new TextBox
            {
                Header = "展示名称",
                PlaceholderText = "例如：输入路径"
            };
            panel.Children.Add(_displayNameBox);

_controlTypeCombo = new ComboBox
        {
            Header = "表单控件类型",
            MinWidth = 180
        };
        foreach (var controlType in ScriptParameterControlTypes.All)
        {
            _controlTypeCombo.Items.Add(ScriptParameterControlTypes.GetDisplayName(controlType));
        }
            _controlTypeCombo.SelectionChanged += ControlTypeCombo_SelectionChanged;
            panel.Children.Add(_controlTypeCombo);

            _argumentNameBox = new TextBox
            {
                Header = "命令行参数名",
                PlaceholderText = "例如：-input / --output，留空则只传值"
            };
            panel.Children.Add(_argumentNameBox);

            _placeholderBox = new TextBox
            {
                Header = "占位提示",
                PlaceholderText = "运行表单中的提示文案"
            };
            panel.Children.Add(_placeholderBox);

            _defaultTextBox = new TextBox
            {
                Header = "默认值",
                PlaceholderText = "可选"
            };
            panel.Children.Add(_defaultTextBox);

            _defaultToggle = new ToggleSwitch
            {
                Header = "默认开关值",
                OnContent = "开启",
                OffContent = "关闭",
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_defaultToggle);

            _helpTextBox = new TextBox
            {
                Header = "帮助说明",
                PlaceholderText = "可选，显示在运行表单里",
                AcceptsReturn = true,
                Height = 72,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_helpTextBox);

            _requiredToggle = new ToggleSwitch
            {
                Header = "是否必填",
                OnContent = "必填",
                OffContent = "可选"
            };
            panel.Children.Add(_requiredToggle);

            _sortOrderBox = new NumberBox
            {
                Header = "显示顺序",
                Minimum = 1,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            panel.Children.Add(_sortOrderBox);

            _errorText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_errorText);

            Content = panel;
            LoadInitialValues();
        }

private void LoadInitialValues()
    {
        _nameBox.Text = _parameter.Name;
        _displayNameBox.Text = _parameter.DisplayName;
        var controlType = string.IsNullOrWhiteSpace(_parameter.ControlType)
            ? ScriptParameterControlTypes.Text
            : _parameter.ControlType;
        _controlTypeCombo.SelectedItem = ScriptParameterControlTypes.GetDisplayName(controlType);
        _argumentNameBox.Text = _parameter.ArgumentName;
        _placeholderBox.Text = _parameter.Placeholder;
        _defaultTextBox.Text = _parameter.DefaultValue;
        _defaultToggle.IsOn = string.Equals(_parameter.DefaultValue, "true", StringComparison.OrdinalIgnoreCase);
        _helpTextBox.Text = _parameter.HelpText;
        _requiredToggle.IsOn = _parameter.IsRequired;
        _sortOrderBox.Value = _parameter.SortOrder <= 0 ? 1 : _parameter.SortOrder;
        UpdateDefaultValueControl();
    }

        private void ControlTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDefaultValueControl();
        }

private void UpdateDefaultValueControl()
    {
        var controlType = _controlTypeCombo.SelectedItem as string ?? ScriptParameterControlTypes.GetDisplayName(ScriptParameterControlTypes.Text);
        var isBoolean = string.Equals(controlType, ScriptParameterControlTypes.GetDisplayName(ScriptParameterControlTypes.Boolean), StringComparison.OrdinalIgnoreCase);

        _defaultTextBox.Visibility = isBoolean ? Visibility.Collapsed : Visibility.Visible;
        _defaultToggle.Visibility = isBoolean ? Visibility.Visible : Visibility.Collapsed;
    }

private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _errorText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorText.Text = "参数名称不能为空";
            args.Cancel = true;
            return;
        }

        if (_controlTypeCombo.SelectedItem is not string displayName)
        {
            _errorText.Text = "请选择控件类型";
            args.Cancel = true;
            return;
        }

        var controlType = GetControlTypeFromDisplayName(displayName);
        _parameter.Name = _nameBox.Text.Trim();
        _parameter.DisplayName = string.IsNullOrWhiteSpace(_displayNameBox.Text)
            ? _parameter.Name
            : _displayNameBox.Text.Trim();
        _parameter.ControlType = controlType;
        _parameter.ArgumentName = _argumentNameBox.Text.Trim();
        _parameter.Placeholder = _placeholderBox.Text.Trim();
        _parameter.HelpText = _helpTextBox.Text.Trim();
        _parameter.IsRequired = _requiredToggle.IsOn;
        _parameter.SortOrder = (int)Math.Round(_sortOrderBox.Value);
        _parameter.DefaultValue = string.Equals(controlType, ScriptParameterControlTypes.Boolean, StringComparison.OrdinalIgnoreCase)
            ? (_defaultToggle.IsOn ? "true" : "false")
            : _defaultTextBox.Text;
    }

    private static string GetControlTypeFromDisplayName(string displayName)
    {
        return displayName switch
        {
            var dn when dn == ScriptParameterControlTypes.GetDisplayName(ScriptParameterControlTypes.Text) => ScriptParameterControlTypes.Text,
            var dn when dn == ScriptParameterControlTypes.GetDisplayName(ScriptParameterControlTypes.Multiline) => ScriptParameterControlTypes.Multiline,
            var dn when dn == ScriptParameterControlTypes.GetDisplayName(ScriptParameterControlTypes.Boolean) => ScriptParameterControlTypes.Boolean,
            _ => ScriptParameterControlTypes.Text
        };
    }

        private static ScriptParameterDefinition Clone(ScriptParameterDefinition source)
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
    }
}
