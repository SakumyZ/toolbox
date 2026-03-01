using System;
using System.Runtime.InteropServices;

namespace ToolBox.Services
{
    /// <summary>
    /// 全局热键服务，使用 Win32 RegisterHotKey API
    /// </summary>
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 修饰键常量
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;

        // 虚拟键码
        private const uint VK_S = 0x53; // S 键
        private const uint VK_N = 0x4E; // N 键

        /// <summary>
        /// 热键 ID：快速搜索 (Ctrl+Alt+S)
        /// </summary>
        public const int HOTKEY_QUICK_SEARCH = 9001;

        /// <summary>
        /// 热键 ID：快速新增 (Ctrl+Alt+N)
        /// </summary>
        public const int HOTKEY_QUICK_ADD = 9002;

        /// <summary>
        /// WM_HOTKEY 消息 ID
        /// </summary>
        public const int WM_HOTKEY = 0x0312;

        private IntPtr _hWnd;
        private bool _registered;

        /// <summary>
        /// 快速搜索热键触发事件
        /// </summary>
        public event Action? QuickSearchRequested;

        /// <summary>
        /// 快速新增热键触发事件
        /// </summary>
        public event Action? QuickAddRequested;

        /// <summary>
        /// 注册全局热键
        /// </summary>
        public bool Register(IntPtr hWnd)
        {
            _hWnd = hWnd;

            var result1 = RegisterHotKey(hWnd, HOTKEY_QUICK_SEARCH,
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_S);

            var result2 = RegisterHotKey(hWnd, HOTKEY_QUICK_ADD,
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);

            _registered = result1 || result2;
            return _registered;
        }

        /// <summary>
        /// 处理 WM_HOTKEY 消息
        /// </summary>
        public void HandleHotkeyMessage(int hotkeyId)
        {
            switch (hotkeyId)
            {
                case HOTKEY_QUICK_SEARCH:
                    QuickSearchRequested?.Invoke();
                    break;
                case HOTKEY_QUICK_ADD:
                    QuickAddRequested?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// 取消注册所有热键
        /// </summary>
        public void Unregister()
        {
            if (_registered && _hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_QUICK_SEARCH);
                UnregisterHotKey(_hWnd, HOTKEY_QUICK_ADD);
                _registered = false;
            }
        }

        public void Dispose()
        {
            Unregister();
            GC.SuppressFinalize(this);
        }
    }
}
