using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// Dashboard 卡片提供者接口。
    /// 各模块实现此接口以在 Dashboard 上展示收藏内容。
    /// </summary>
    public interface IDashboardCardProvider
    {
        /// <summary>
        /// 卡片标题。
        /// </summary>
        string CardTitle { get; }

        /// <summary>
        /// 卡片图标（Segoe MDL2 Assets 字形）。
        /// </summary>
        string IconGlyph { get; }

        /// <summary>
        /// 点击卡片标题时导航到哪个模块页。
        /// </summary>
        string NavigationTag { get; }

        /// <summary>
        /// 获取收藏项目列表。
        /// </summary>
        Task<IReadOnlyList<DashboardFavoriteItem>> GetFavoritesAsync();

        /// <summary>
        /// 收藏数据变化时触发，Dashboard 据此刷新卡片。
        /// </summary>
        event EventHandler? FavoritesChanged;
    }
}
