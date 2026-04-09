using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBox.Models;

namespace ToolBox.Services
{
    /// <summary>
    /// 提醒调度服务，在应用运行期间轮询并触发通知
    /// </summary>
    public sealed class ReminderSchedulerService : IDisposable
    {
        private static readonly Lazy<ReminderSchedulerService> _instance =
            new(() => new ReminderSchedulerService());

        private readonly ReminderService _reminderService = new();
        private readonly NotificationService _notificationService = new();
        private readonly SemaphoreSlim _checkLock = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _backgroundTask;

        public static ReminderSchedulerService Instance => _instance.Value;

        private ReminderSchedulerService()
        {
        }

        /// <summary>
        /// 启动调度器
        /// </summary>
        public void Start()
        {
            if (_backgroundTask != null)
            {
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _backgroundTask = Task.Run(() => RunLoopAsync(_cancellationTokenSource.Token));
            _ = CheckNowAsync();
        }

        /// <summary>
        /// 立即检查一次到期提醒
        /// </summary>
        public async Task CheckNowAsync()
        {
            if (_cancellationTokenSource == null)
            {
                return;
            }

            await CheckDueRemindersAsync(_cancellationTokenSource.Token);
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    await CheckDueRemindersAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task CheckDueRemindersAsync(CancellationToken cancellationToken)
        {
            if (!await _checkLock.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                var reminders = _reminderService
                    .GetAllReminders()
                    .Where(r => r.IsEnabled)
                    .ToList();

                foreach (var reminder in reminders)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!ShouldTrigger(reminder, now))
                    {
                        continue;
                    }

                    var success = _notificationService.ShowReminderNotification(reminder, out var errorMessage);
                    _reminderService.RecordTrigger(
                        reminder.Id,
                        now,
                        success ? "Success" : $"Failed: {errorMessage}",
                        success);

                    if (success && reminder.RecurrenceType == ReminderRecurrenceTypes.Single)
                    {
                        _reminderService.SetReminderEnabled(reminder.Id, false);
                    }
                }
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private static bool ShouldTrigger(Reminder reminder, DateTime now)
        {
            return reminder.RecurrenceType switch
            {
                ReminderRecurrenceTypes.Interval => ShouldTriggerInterval(reminder, now),
                ReminderRecurrenceTypes.Daily => ShouldTriggerDaily(reminder, now),
                ReminderRecurrenceTypes.Monthly => ShouldTriggerMonthly(reminder, now),
                _ => ShouldTriggerSingle(reminder, now)
            };
        }

        private static bool ShouldTriggerSingle(Reminder reminder, DateTime now)
        {
            if (!TimeSpan.TryParse(reminder.TimeText, out var timeOfDay))
            {
                return false;
            }

            var scheduledAt = now.Date.Add(timeOfDay);
            if (now < scheduledAt || scheduledAt.AddMinutes(1) <= now)
            {
                return false;
            }

            return !reminder.LastTriggeredAt.HasValue;
        }

        private static bool ShouldTriggerDaily(Reminder reminder, DateTime now)
        {
            if (!TimeSpan.TryParse(reminder.TimeText, out var timeOfDay))
            {
                return false;
            }

            var scheduledAt = now.Date.Add(timeOfDay);
            if (now < scheduledAt || scheduledAt.AddMinutes(1) <= now)
            {
                return false;
            }

            return reminder.LastTriggeredAt?.Date != now.Date;
        }

        private static bool ShouldTriggerMonthly(Reminder reminder, DateTime now)
        {
            if (!TimeSpan.TryParse(reminder.TimeText, out var timeOfDay))
            {
                return false;
            }

            var targetDay = reminder.DayOfMonth <= 0 ? 1 : reminder.DayOfMonth;
            var currentDay = Math.Min(targetDay, DateTime.DaysInMonth(now.Year, now.Month));
            if (now.Day != currentDay)
            {
                return false;
            }

            var scheduledAt = new DateTime(now.Year, now.Month, currentDay, timeOfDay.Hours, timeOfDay.Minutes, 0);
            if (now < scheduledAt || scheduledAt.AddMinutes(1) <= now)
            {
                return false;
            }

            return !reminder.LastTriggeredAt.HasValue ||
                reminder.LastTriggeredAt.Value.Year != now.Year ||
                reminder.LastTriggeredAt.Value.Month != now.Month;
        }

        private static bool ShouldTriggerInterval(Reminder reminder, DateTime now)
        {
            var intervalMinutes = reminder.IntervalMinutes <= 0 ? 60 : reminder.IntervalMinutes;
            if (!reminder.LastTriggeredAt.HasValue)
            {
                return true;
            }

            return reminder.LastTriggeredAt.Value.AddMinutes(intervalMinutes) <= now;
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _notificationService.Unregister();
            _checkLock.Dispose();
        }
    }
}