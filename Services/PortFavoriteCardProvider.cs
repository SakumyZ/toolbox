using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// 端口收藏的 Dashboard 卡片提供者。
    /// </summary>
    public class PortFavoriteCardProvider : IDashboardCardProvider
    {
        private readonly PortFavoriteService _favoriteService;

        public PortFavoriteCardProvider(PortFavoriteService favoriteService)
        {
            _favoriteService = favoriteService;
        }

        public string CardTitle => "端口收藏";

        public string IconGlyph => "\xE774";

        public string NavigationTag => "PortViewer";

        public event EventHandler? FavoritesChanged;

        /// <summary>
        /// 通知收藏数据已变化（由 PortViewerPage 在切换收藏后调用）。
        /// </summary>
        public void NotifyFavoritesChanged()
        {
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<IReadOnlyList<DashboardFavoriteItem>> GetFavoritesAsync()
        {
            var ports = _favoriteService.GetFavoritePorts();
            var listeningPorts = new HashSet<int>(
                PortService.GetListeningPorts().Select(p => p.LocalPort));

            var items = ports
                .OrderBy(p => p)
                .Select(p =>
                {
                    bool isListening = listeningPorts.Contains(p);

                    return new DashboardFavoriteItem
                    {
                        Title = $"端口 {p}",
                        TypeText = PortService.CommonDevPorts.Contains(p)
                            ? "常用开发端口"
                            : "自定义收藏",
                        StatusText = isListening ? "LISTEN" : "未监听",
                        StatusBrush = isListening
                            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                Windows.UI.Color.FromArgb(255, 16, 137, 62))
                            : new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                Windows.UI.Color.FromArgb(255, 140, 140, 140)),
                        NavigationTag = NavigationTag,
                        NavigationParameter = p
                    };
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<DashboardFavoriteItem>>(items);
        }
    }
}
