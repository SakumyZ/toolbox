using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Models;
using ToolBox.Services;
using Serilog;

namespace ToolBox.Views
{
    public sealed partial class BackupDataManagerDialog : ContentDialog
    {
        private readonly string _url;
        private readonly string _username;
        private readonly string _password;
        private readonly string _directory;
        private readonly BackupService _backupService;
        private readonly ObservableCollection<BackupItemViewModel> _backupItems = new();
        private bool _isUpdatingSelection = false;

        public BackupDataManagerDialog(string url, string username, string password, string directory)
        {
            InitializeComponent();
            _url = url;
            _username = username;
            _password = password;
            _directory = directory;
            _backupService = new BackupService();
            BackupListView.ItemsSource = _backupItems;
            
            // 采用 Resource 覆盖弹窗容器本身的大小，不要在此处设置 this.MaxWidth，否则会破坏全屏遮罩和居中

        }

        private async void ContentDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            BackupListView.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            SelectAllCheckBox.IsChecked = false;
            _backupItems.Clear();
            UpdateDeleteButtonState();

            try
            {
                var files = await _backupService.GetRemoteBackupFilesAsync(_url, _username, _password, _directory);
                foreach (var f in files)
                {
                    var vm = new BackupItemViewModel(f);
                    vm.PropertyChanged += ItemViewModel_PropertyChanged;
                    _backupItems.Add(vm);
                }

                if (_backupItems.Count == 0)
                {
                    EmptyPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    BackupListView.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupDataManagerDialog: 加载备份数据列表失败");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            foreach (var item in _backupItems)
            {
                item.IsSelected = true;
            }
            _isUpdatingSelection = false;
            UpdateDeleteButtonState();
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            foreach (var item in _backupItems)
            {
                item.IsSelected = false;
            }
            _isUpdatingSelection = false;
            UpdateDeleteButtonState();
        }

        private void ItemViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BackupItemViewModel.IsSelected))
            {
                ItemCheckBox_CheckedChanged(this, new RoutedEventArgs());
            }
        }

        private void ItemCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelection) return;
            UpdateDeleteButtonState();
        }

        private void UpdateDeleteButtonState()
        {
            var selectedCount = _backupItems.Count(item => item.IsSelected);
            DeleteSelectedButton.IsEnabled = selectedCount > 0;
            DeleteButtonText.Text = $"删除选中 ({selectedCount})";

            _isUpdatingSelection = true;
            if (_backupItems.Count > 0 && selectedCount == _backupItems.Count)
            {
                SelectAllCheckBox.IsChecked = true;
            }
            else if (selectedCount == 0)
            {
                SelectAllCheckBox.IsChecked = false;
            }
            else
            {
                SelectAllCheckBox.IsChecked = null; // 半选状态
            }
            _isUpdatingSelection = false;
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _backupItems.Where(item => item.IsSelected).ToList();
            if (selectedItems.Count == 0) return;

            var confirmDialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要从 WebDAV 服务器上删除选中的 {selectedItems.Count} 个备份包吗？此操作不可逆！",
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            BackupListView.Visibility = Visibility.Collapsed;

            try
            {
                foreach (var item in selectedItems)
                {
                    await _backupService.DeleteRemoteFileAsync(_url, _username, _password, _directory, item.FileName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupDataManagerDialog: 批量删除备份文件失败");
            }

            await LoadDataAsync();
        }

        private async void RestoreItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not BackupItemViewModel target) return;

            var confirmDialog = new ContentDialog
            {
                Title = "确认从备份恢复",
                Content = $"确定要恢复至历史备份节点：{target.FileName} 吗？\n该操作将完全覆盖本地所有当前数据且无法撤销！应用将自动重启以应用数据更改。",
                PrimaryButtonText = "确认恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            BackupListView.Visibility = Visibility.Collapsed;

            var tempZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox", "TempDownload.zip");
            try
            {
                await _backupService.DownloadBackupFromWebDavAsync(_url, _username, _password, _directory, target.FileName, tempZip);
                _backupService.RestoreFromZip(tempZip);

                var successDialog = new ContentDialog
                {
                    Title = "恢复成功",
                    Content = "历史配置已成功还原，应用需要立即重启以加载新数据。",
                    PrimaryButtonText = "立即重启",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };
                
                successDialog.PrimaryButtonClick += (s, args) =>
                {
                    Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
                };

                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupDataManagerDialog: 恢复备份时发生异常");
                
                var errorDialog = new ContentDialog
                {
                    Title = "恢复失败",
                    Content = $"拉取或恢复备份时失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
                await LoadDataAsync();
            }
            finally
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// 备份项的 ViewModel，用于支持 CheckBox 选中通知
    /// </summary>
    public class BackupItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string FileName => Model.FileName;
        public string DisplayTime => Model.DisplayTime;
        public string DisplaySize => Model.DisplaySize;
        public WebDavBackupItem Model { get; }

        public BackupItemViewModel(WebDavBackupItem model)
        {
            Model = model;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
