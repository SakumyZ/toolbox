using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ToolBox.Models;
using Windows.Graphics;
using Windows.Storage.Streams; // 新增：用于处理内存流

namespace ToolBox.Views
{
    /// <summary>
    /// 应用内通知窗，采用左图右文的双栏布局。
    /// </summary>
    public sealed partial class NotificationWindow : Window
    {
        /// <summary>
        /// 通知窗宽度。
        /// </summary>
        public const int WindowWidth = 420;

        /// <summary>
        /// 通知窗高度。
        /// </summary>
        public const int WindowHeight = 104;

        /// <summary>
        /// 紧凑通知窗高度。
        /// </summary>
        public const int CompactWindowHeight = 84;

        /// <summary>
        /// 当前通知窗实际高度。
        /// </summary>
        public int CurrentWindowHeight { get; private set; } = WindowHeight;

        private readonly DispatcherQueueTimer _dismissTimer;

        /// <summary>
        /// 初始化通知窗。
        /// </summary>
        /// <param name="visual">通知展示内容。</param>
        public NotificationWindow(NotificationVisual visual)
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            ConfigureWindow();
            ApplyAppearance(visual);
            ApplyVisual(visual);

            _dismissTimer = DispatcherQueue.CreateTimer();
            _dismissTimer.Interval = TimeSpan.FromMilliseconds(NormalizeAutoCloseMilliseconds(visual.AutoCloseMilliseconds));
            _dismissTimer.IsRepeating = false;
            _dismissTimer.Tick += DismissTimer_Tick;
            _dismissTimer.Start();
        }

        /// <summary>
        /// 移动到指定位置。
        /// </summary>
        /// <param name="position">目标坐标。</param>
        public void MoveTo(PointInt32 position)
        {
            AppWindow.Move(position);
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }

        /// <summary>
        /// 应用主题和布局样式设置。
        /// </summary>
        private void ApplyAppearance(NotificationVisual visual)
        {
            // 根据设置强制通知主题，System 则跟随系统。
            NotificationRoot.RequestedTheme = visual.ColorTheme switch
            {
                NotificationColorTheme.Light => ElementTheme.Light,
                NotificationColorTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            // 根据设置切换通知布局尺寸。
            if (visual.DisplayStyle == NotificationDisplayStyle.Compact)
            {
                CurrentWindowHeight = CompactWindowHeight;
                MessageText.MaxLines = 1;
                MessageText.Margin = new Thickness(0, 2, 0, 4);
            }
            else
            {
                CurrentWindowHeight = WindowHeight;
                MessageText.MaxLines = 2;
                MessageText.Margin = new Thickness(0, 4, 0, 6);
            }

            AppWindow.Resize(new SizeInt32(WindowWidth, CurrentWindowHeight));

            // 修复浅色主题下主体灰色、边框或覆盖层问题：对 Light 主题使用白色背景并移除图像遮罩。
            try
            {
                if (visual.ColorTheme == NotificationColorTheme.Light)
                {
                    RootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                    RootBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                    // 图片栏不要深色遮罩，使头像在浅色主题下清晰
                    ImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                    // 文字颜色使用深色，保证可读性
                    TitleText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                    MessageText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    FooterText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                }
                else if (visual.ColorTheme == NotificationColorTheme.Dark)
                {
                    // 恢复为资源主题色以支持深色模式
                    RootBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                    ImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0x00, 0x00, 0x00));
                    TitleText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                    MessageText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    FooterText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                }
                else
                {
                    // System：使用资源默认行为
                    RootBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                    ImageOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0x00, 0x00, 0x00));
                }
            }
            catch { }
        }

        /// <summary>
        /// 应用通知视觉数据（已升级支持 SVG 路径与 SVG 源码字符串）
        /// </summary>
        private async void ApplyVisual(NotificationVisual visual)
        {
            TitleText.Text = string.IsNullOrWhiteSpace(visual.Title) ? "提醒" : visual.Title;
            MessageText.Text = string.IsNullOrWhiteSpace(visual.Message) ? "到时间了。" : visual.Message;
            FooterText.Text = visual.FooterText;

            if (string.IsNullOrWhiteSpace(visual.ImageUri))
            {
                return;
            }

            Console.WriteLine($"[NOTIF] Loading image: {visual.ImageUri}");

            try
            {
                // 情况 1：如果传入的是原始的 <svg ...> 源码字符串
                if (visual.ImageUri.Trim().StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svgSource = new SvgImageSource();
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                        {
                            writer.WriteString(visual.ImageUri);
                            await writer.StoreAsync();
                        }
                        await svgSource.SetSourceAsync(stream);
                    }
                    PreviewImage.Source = svgSource;
                }
                // 情况 2：如果是以 .svg 结尾的文件路径或应用资源定位符 (ms-appx:///)
                else if (visual.ImageUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(visual.ImageUri);
                    Console.WriteLine($"[NOTIF] Creating SvgImageSource with URI: {uri}");
                    PreviewImage.Source = new SvgImageSource(uri);
                }
                // 情况 3：普通的图片格式（PNG, JPG, GIF）
                else
                {
                    PreviewImage.Source = new BitmapImage(new Uri(visual.ImageUri));
                }
            }
            catch (Exception ex)
            {
                // 容错处理：若解析失败，可选择清空或挂载默认缺省图
                Console.WriteLine($"[NOTIF] Failed to load image: {ex.Message}");
                Console.WriteLine($"[NOTIF] Stack trace: {ex.StackTrace}");
                PreviewImage.Source = null;
            }
        }

        /// <summary>
        /// 规范化自动关闭时长，避免异常值导致闪退或无法自动关闭。
        /// </summary>
        private static int NormalizeAutoCloseMilliseconds(int milliseconds)
        {
            // 非法或过小值，回退默认值。
            if (milliseconds < NotificationAutoCloseDurations.Minimum)
            {
                return NotificationAutoCloseDurations.Default;
            }

            // 过大值裁剪到上限。
            if (milliseconds > NotificationAutoCloseDurations.Maximum)
            {
                return NotificationAutoCloseDurations.Maximum;
            }

            return milliseconds;
        }

        private void DismissTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _dismissTimer.Stop();
            Close();
        }

        private void NotificationRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _dismissTimer.Stop();
        }

        private void NotificationRoot_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // 鼠标离开后重新启动自动关闭，避免用户阅读时被打断。
            _dismissTimer.Stop();
            _dismissTimer.Start();
        }
    }
}