using Microsoft.UI.Xaml.Media;

namespace ToolBox.Models
{
    /// <summary>
    /// Dashboard 收藏项数据模型。
    /// </summary>
    public class DashboardFavoriteItem
    {
        /// <summary>
        /// 项目标题。
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 分类文本（如"常用开发端口"），可为空。
        /// </summary>
        public string? TypeText { get; set; }

        /// <summary>
        /// 状态文本（如"LISTEN"），为空则不显示 badge。
        /// </summary>
        public string? StatusText { get; set; }

        /// <summary>
        /// 状态 badge 背景色。
        /// </summary>
        public SolidColorBrush? StatusBrush { get; set; }

        /// <summary>
        /// 点击后导航到哪个模块页（对应 NavigationViewItem 的 Tag）。
        /// </summary>
        public string? NavigationTag { get; set; }

        /// <summary>
        /// 导航参数（如端口号），传给目标 Frame。
        /// </summary>
        public object? NavigationParameter { get; set; }
    }
}
