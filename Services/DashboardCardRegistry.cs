using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolBox.Services
{
    /// <summary>
    /// Dashboard 卡片提供者注册中心。
    /// 各模块在初始化时调用 Register 注册自己的卡片提供者。
    /// </summary>
    public static class DashboardCardRegistry
    {
        private static readonly List<IDashboardCardProvider> _providers = new();

        /// <summary>
        /// 所有已注册的卡片提供者变化时触发。
        /// </summary>
        public static event EventHandler? ProvidersChanged;

        /// <summary>
        /// 注册一个卡片提供者。
        /// </summary>
        public static void Register(IDashboardCardProvider provider)
        {
            if (_providers.Any(p => p.NavigationTag == provider.NavigationTag))
            {
                return;
            }

            _providers.Add(provider);
            provider.FavoritesChanged += OnProviderFavoritesChanged;
            ProvidersChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// 获取所有已注册的卡片提供者。
        /// </summary>
        public static IReadOnlyList<IDashboardCardProvider> GetAll()
        {
            return _providers.AsReadOnly();
        }

        private static void OnProviderFavoritesChanged(object? sender, EventArgs e)
        {
            ProvidersChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
