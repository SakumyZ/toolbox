using System;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ToolBox.Models;
using Serilog;

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
            Log.Information("NotificationService: 正在初始化 Windows 通知系统...");

            try
            {
                AppNotificationManager.Default.Register();
                Log.Information("NotificationService: Windows 通知系统注册成功");
            }
            catch (COMException ex)
            {
                // 当前功能只需要本地弹出通知，不处理通知点击激活。
                // 某些启动方式下没有配置通知激活 COM 清单时，Register 会失败；
                // 此时仍允许应用继续启动，并在发送通知时直接调用 Show。
                Log.Warning("NotificationService: Windows 通知注册失败 (COMException)，可能因非 MSIX 运行环境导致，将降级忽略: {Message}", ex.Message);
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

            Log.Information("NotificationService: 正在注销 Windows 通知系统...");
            try
            {
                AppNotificationManager.Default.Unregister();
                Log.Information("NotificationService: Windows 通知系统注销成功");
            }
            catch (COMException ex)
            {
                Log.Warning("NotificationService: Windows 通知注销失败 (COMException): {Message}", ex.Message);
            }

            _isInitialized = false;
            _registerAttempted = false;
        }

        /// <summary>
        /// 显示提醒通知
        /// </summary>
        public bool ShowReminderNotification(Reminder reminder, out string? errorMessage)
        {
            Log.Information("NotificationService: 准备发送系统通知 -> 标题: '{Title}', 内容: '{Message}'", reminder.Title, reminder.Message);
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
                Log.Error(ex, "NotificationService: 发送通知失败 '{Title}'", reminder.Title);
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}