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
        private readonly ScriptManagerService _scriptManagerService = new();
        private readonly SemaphoreSlim _checkLock = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _backgroundTask;

        public static ReminderSchedulerService Instance => _instance.Value;

        /// <summary>
        /// 提醒状态或触发日志发生变化。
        /// </summary>
        public event EventHandler? RemindersChanged;

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

                    bool success = false;
                    string? errorMessage = string.Empty;

                    if (reminder.ActionType == ReminderActionTypes.Script && reminder.ScriptId.HasValue)
                    {
                        var script = _scriptManagerService.GetScript(reminder.ScriptId.Value);
                        if (script != null)
                        {
                            var parameters = new System.Collections.Generic.Dictionary<long, string>();
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(reminder.ScriptParameters))
                                {
                                    var parsedParams = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(reminder.ScriptParameters);
                                    if (parsedParams != null)
                                    {
                                        foreach (var p in script.Parameters)
                                        {
                                            if (parsedParams.TryGetValue(p.Name, out var val))
                                            {
                                                parameters[p.Id] = val;
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // 忽略参数解析错误
                            }

                            var scriptPath = _scriptManagerService.GetScriptAbsolutePath(script);
                            var workingDirectory = System.IO.File.Exists(scriptPath)
                                ? System.IO.Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory
                                : string.Empty;
                            var logId = _scriptManagerService.CreateExecutionLog(
                                script,
                                ScriptExecutionSources.Reminder,
                                reminder.Id,
                                now,
                                _scriptManagerService.BuildCommandPreview(script, scriptPath, parameters),
                                workingDirectory,
                                parameters);
                            var triggerLogId = _reminderService.RecordTrigger(
                                reminder.Id,
                                now,
                                "Script Running",
                                true,
                                logId);
                            OnRemindersChanged();

                            // 异步执行，不阻塞调度器
                            _ = Task.Run(async () =>
                            {
                                var result = await _scriptManagerService.ExecuteScriptAsync(
                                    script,
                                    parameters,
                                    null,
                                    null,
                                    ScriptExecutionSources.Reminder,
                                    reminder.Id,
                                    logId);
                                _reminderService.UpdateTriggerStatus(
                                    triggerLogId,
                                    result.Success ? "Script Execution Success" : $"Script Failed: {result.Message}");
                                OnRemindersChanged();

                                var notifyReminder = new Reminder
                                {
                                    Title = result.Success ? $"脚本 [{script.Name}] 执行成功" : $"脚本 [{script.Name}] 执行失败",
                                    Message = result.Success ? $"按计划在后台运行完毕。\n{result.Message}" : $"发生错误：\n{result.Message}",
                                    TimeText = reminder.TimeText
                                };
                                _notificationService.ShowReminderNotification(notifyReminder, out _);
                            });
                            
                            // 对于定时脚本，我们默认触发就是成功的（至于脚本执行是否成功在后台记录）
                            success = true; 
                        }
                        else
                        {
                            success = false;
                            errorMessage = "Script not found";
                        }
                    }
                    else
                    {
                        success = _notificationService.ShowReminderNotification(reminder, out errorMessage);
                    }

                    if (reminder.ActionType != ReminderActionTypes.Script)
                    {
                        _reminderService.RecordTrigger(
                            reminder.Id,
                            now,
                            success ? "Success" : $"Failed: {errorMessage}",
                            success);
                        OnRemindersChanged();
                    }
                    else if (!success)
                    {
                        _reminderService.RecordTrigger(
                            reminder.Id,
                            now,
                            $"Failed: {errorMessage}",
                            false);
                        OnRemindersChanged();
                    }

                    if (success && reminder.RecurrenceType == ReminderRecurrenceTypes.Single)
                    {
                        _reminderService.SetReminderEnabled(reminder.Id, false);
                        OnRemindersChanged();
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

            DateTime targetDate = now.Date;
            bool hasExplicitDate = false;
            if (!string.IsNullOrWhiteSpace(reminder.DateText) && DateTime.TryParse(reminder.DateText, out var parsedDate))
            {
                hasExplicitDate = true;
                targetDate = parsedDate.Date;
            }

            var scheduledAt = targetDate.Add(timeOfDay);
            if (!hasExplicitDate && scheduledAt.AddMinutes(1) <= now)
            {
                scheduledAt = scheduledAt.AddDays(1);
            }

            if (hasExplicitDate)
            {
                if (now < scheduledAt)
                {
                    return false;
                }
            }
            else
            {
                if (now < scheduledAt || scheduledAt.AddMinutes(1) <= now)
                {
                    return false;
                }
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

        private void OnRemindersChanged()
        {
            RemindersChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
