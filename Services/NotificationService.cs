using System;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// Windows 默认通知服务
    /// </summary>
    public class NotificationService
    {
        private bool _isInitialized;
        private bool _registerAttempted;

        /// <summary>
        /// 初始化通知系统。
        /// 仅在需要处理通知点击回调时才需要真正注册 COM 激活。
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized || _registerAttempted)
            {
                return;
            }

            _registerAttempted = true;

            try
            {
                AppNotificationManager.Default.Register();
            }
            catch (COMException)
            {
                // 当前功能只需要本地弹出通知，不处理通知点击激活。
                // 某些启动方式下没有配置通知激活 COM 清单时，Register 会失败；
                // 此时仍允许应用继续启动，并在发送通知时直接调用 Show。
            }

            _isInitialized = true;
        }

        /// <summary>
        /// 释放通知资源
        /// </summary>
        public void Unregister()
        {
            if (!_registerAttempted)
            {
                return;
            }

            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (COMException)
            {
            }

            _isInitialized = false;
            _registerAttempted = false;
        }

        /// <summary>
        /// 显示提醒通知
        /// </summary>
        public bool ShowReminderNotification(Reminder reminder, out string? errorMessage)
        {
            try
            {
                Initialize();

                var bodyText = string.IsNullOrWhiteSpace(reminder.Message)
                    ? $"提醒时间：{reminder.TimeText}"
                    : reminder.Message;

                var notification = new AppNotificationBuilder()
                    .AddText(reminder.Title)
                    .AddText(bodyText)
                    .BuildNotification();

                AppNotificationManager.Default.Show(notification);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}