using System;
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
    /// 快速搜索窗口 - 全局热键呼出
    /// </summary>
    public sealed partial class QuickSearchWindow : Window
    {
        private readonly DatabaseService _db = DatabaseService.Instance;
        private readonly ObservableCollection<QuickSearchItem> _results = new();

        public QuickSearchWindow()
        {
            InitializeComponent();

            // 设置窗口大小
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32(600, 500));

            // 居中显示
            CenterWindow();

            ResultList.ItemsSource = _results;

            // 默认加载最近使用的片段
            LoadRecentSnippets();

            ExtendsContentIntoTitleBar = true;
        }

        private void CenterWindow()
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            var x = (workArea.Width - 600) / 2;
            var y = (workArea.Height - 500) / 3; // 偏上 1/3 位置
            this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        private void LoadRecentSnippets()
        {
            _results.Clear();
            var snippets = _db.GetSnippets(orderBy: "UsageCount DESC");
            foreach (var s in snippets.Take(20))
            {
                _results.Add(new QuickSearchItem(s));
            }
        }

        private void SearchInput_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                PerformSearch(sender.Text);
            }
        }

        private void SearchInput_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            // 如果列表有结果，选中第一项并复制
            if (_results.Count > 0)
            {
                if (ResultList.SelectedItem is QuickSearchItem selected)
                {
                    CopyAndClose(selected);
                }
                else
                {
                    CopyAndClose(_results[0]);
                }
            }
        }

        private void SearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                this.Close();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down && _results.Count > 0)
            {
                // 焦点移到列表
                ResultList.SelectedIndex = 0;
                ResultList.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void ResultList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ResultList.SelectedItem is QuickSearchItem item)
            {
                CopyAndClose(item);
            }
        }

        private void ResultList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && ResultList.SelectedItem is QuickSearchItem item)
            {
                CopyAndClose(item);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        }

        private void PerformSearch(string? searchText)
        {
            _results.Clear();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                LoadRecentSnippets();
                return;
            }

            var snippets = _db.GetSnippets(searchText: searchText);
            foreach (var s in snippets.Take(20))
            {
                _results.Add(new QuickSearchItem(s));
            }
        }

        private void CopyAndClose(QuickSearchItem item)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(item.Model.Code);
            Clipboard.SetContent(dataPackage);

            _db.IncrementUsageCount(item.Model.Id);
            this.Close();
        }
    }

    /// <summary>
    /// 快速搜索结果项
    /// </summary>
    public class QuickSearchItem
    {
        public Snippet Model { get; }

        public string Title => Model.Title;
        public string Language => Model.Language;
        public string Tags => Model.Tags;

        public string CodePreview
        {
            get
            {
                var firstLine = Model.Code.Split('\n').FirstOrDefault()?.Trim() ?? "";
                return firstLine.Length > 80 ? firstLine[..80] + "..." : firstLine;
            }
        }

        public Visibility IsFavoriteVisibility =>
            Model.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        public Visibility LanguageVisibility =>
            string.IsNullOrEmpty(Model.Language) ? Visibility.Collapsed : Visibility.Visible;

        public QuickSearchItem(Snippet model)
        {
            Model = model;
        }
    }
}
