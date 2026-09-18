using NodaTime;
using Terminus.Core.Logic;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// Behavior orchestrator (T6) - manages the state machine for sleep cycle warnings and shutdowns.
/// Thread-safe, single entry point for all behavior logic.
/// </summary>
public class BehaviorOrchestrator
{
    private readonly CalendarDataService _calendarService;
    private readonly ShutdownService _shutdownService;
    private readonly INotificationService _notificationService;
    private readonly IClock _clock;
    private readonly object _lock = new();

    private BehaviorContext? _context;
    private Timer? _timer;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Hour (0-23) that separates early-class vs non-early-class days.
    /// Any class starting before this hour is considered "early".
    /// Default is 12 (noon).
    /// </summary>
    public int EarlyClassCutoffHour { get; set; } = 12;

    /// <summary>
    /// Delay quota for early-class days.
    /// Default is 90 minutes.
    /// </summary>
    public TimeSpan EarlyClassDelayQuota { get; set; } = TimeSpan.FromMinutes(90);

    public event EventHandler<BehaviorStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public BehaviorOrchestrator(
        CalendarDataService calendarService,
        ShutdownService shutdownService,
        INotificationService notificationService,
        IClock? clock = null)
    {
        _calendarService = calendarService;
        _shutdownService = shutdownService;
        _notificationService = notificationService;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>
    /// Starts the behavior orchestrator.
    /// </summary>
    public async Task StartAsync(string calendarUrl)
    {
        lock (_lock)
        {
            if (_cts != null)
                return; // Already started

            _cts = new CancellationTokenSource();
        }

        await InitializeContextAsync(calendarUrl);
        StartTimer();
    }

    /// <summary>
    /// Restarts the behavior orchestrator with a new calendar URL.
    /// Stops the current instance and starts fresh.
    /// </summary>
    public async Task RestartAsync(string calendarUrl)
    {
        var oldState = _context?.State ?? BehaviorState.Idle;
        await StopAsync();
        lock (_lock)
        {
            _context = null;
            _cts = new CancellationTokenSource();
        }
        await InitializeContextAsync(calendarUrl);
        StartTimer();

        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx != null)
        {
            StateChanged?.Invoke(this, new BehaviorStateChangedEventArgs
            {
                OldState = oldState,
                NewState = ctx.State,
                CurrentCycle = ctx.CurrentCycle,
                Timing = ctx.Timing
            });
        }
    }

    /// <summary>
    /// Stops the behavior orchestrator.
    /// </summary>
    public async Task StopAsync()
    {
        Timer? timer;
        CancellationTokenSource? cts;

        lock (_lock)
        {
            timer = _timer;
            cts = _cts;
            _timer = null;
            _cts = null;
        }

        timer?.Dispose();

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        await _notificationService.ClearAllNotificationsAsync();
    }

    /// <summary>
    /// User pressed delay button. Accepts custom duration (default 30 min).
    /// </summary>
    public async Task<bool> OnDelayRequestedAsync(TimeSpan? duration = null)
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null || ctx.State != BehaviorState.Warning)
            return false;

        var delayAmount = duration ?? ctx.DelayIncrement;
        var now = _clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);

        // Check quota for early-class days
        if (!ctx.Timing.IsUnlimitedManualDelay)
        {
            if (ctx.CurrentCycle.QuotaRemaining < delayAmount)
            {
                await _notificationService.ShowWarningAsync(new WarningNotificationArgs
                {
                    Title = "配額已用完",
                    Message = $"延遲配額不足（剩餘 {ctx.CurrentCycle.QuotaRemaining.TotalMinutes:F0} 分鐘），無法延遲 {delayAmount.TotalMinutes:F0} 分鐘。",
                    QuotaRemaining = ctx.CurrentCycle.QuotaRemaining,
                    IsUnlimitedDelay = false,
                    ShowDelayButton = true,
                    ShowShutdownButton = true,
                    ShowAIModeButton = true
                });
                return false;
            }

            ctx.CurrentCycle.QuotaRemaining -= delayAmount;
        }

        ctx.CurrentCycle.DelayCount++;
        ctx.NextActionTime = now.Plus(Duration.FromTimeSpan(delayAmount));

        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.Delayed);
        }

        SaveCycleState();

        await _notificationService.ShowDelayConfirmationAsync(
            delayAmount,
            ctx.CurrentCycle.QuotaRemaining,
            ctx.Timing.IsUnlimitedManualDelay);
        return true;
    }

    /// <summary>
    /// User pressed shutdown now button.
    /// </summary>
    public async Task OnShutdownNowRequestedAsync()
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null)
            return;

        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.ShuttingDown);
        }

        await _shutdownService.InitiateShutdownAsync("User requested shutdown", _cts?.Token ?? default);
    }

    /// <summary>
    /// User pressed AI overnight mode button.
    /// </summary>
    public async Task OnAIModeRequestedAsync(string taskDescription)
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null)
            return;

        ctx.IsAIModeActive = true;
        ctx.AITaskDescription = taskDescription;

        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.AIMode);
        }

        await _notificationService.ShowAIModeStartedAsync(taskDescription);
    }

    /// <summary>
    /// AI mode completed callback.
    /// </summary>
    public async Task OnAIModeCompletedAsync(bool success)
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null || !ctx.IsAIModeActive)
            return;

        await _notificationService.ShowAIModeCompletedAsync(
            ctx.AITaskDescription ?? "Unknown task",
            success);

        ctx.IsAIModeActive = false;
        ctx.AITaskDescription = null;

        // Proceed to shutdown
        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.ShuttingDown);
        }

        await _shutdownService.InitiateShutdownAsync("AI mode completed", _cts?.Token ?? default);
    }

    private async Task InitializeContextAsync(string calendarUrl)
    {
        var now = _clock.GetCurrentInstant();
        var cycle = SleepCycleCalculator.GetCurrentCycle(now);
        TryRestoreCycleState(cycle);

        LoggerService.Info($"Orchestrator: InitializeContextAsync 開始, URL={calendarUrl?.Substring(0, Math.Min(50, calendarUrl?.Length ?? 0)) ?? "null"}...");

        // Fetch schedule data
        var (events, status, cacheAge) = await _calendarService.GetScheduleAsync(calendarUrl);
        var hasData = status != CacheStatus.Default;

        LoggerService.Info($"Orchestrator: 日曆數據, 事件數={events.Count}, hasData={hasData}, status={status}");

        // Classify day (using configurable cutoff hour)
        var cutoffHour = EarlyClassCutoffHour;
        var classification = ScheduleClassifier.ClassifyDay(cycle.TargetDate, events, hasData, cutoffHour);
        var firstClassTime = ScheduleClassifier.GetFirstClassTime(cycle.TargetDate, events, cutoffHour);

        LoggerService.Info($"Orchestrator: 日期分類={classification}, 首節課時間={(firstClassTime.HasValue ? firstClassTime.Value.ToString("HH:mm", null) : "無")}, 早課截止={cutoffHour}:00");

        // Calculate timing (using configurable delay quota)
        var timing = TimingCalculator.CalculateTiming(
            classification, firstClassTime,
            earlyClassQuota: EarlyClassDelayQuota);

        LoggerService.Info($"Orchestrator: 預警={timing.PreWarningTime}, 警告={timing.WarningTime}, 硬關機={timing.HardShutdownTime}");

        var context = new BehaviorContext
        {
            CurrentCycle = cycle,
            Timing = timing,
            State = BehaviorState.Idle,
            NextActionTime = null,
            Events = events
        };

        // Safety check: if we're already past the hard shutdown time for this cycle,
        // mark as Disabled to prevent accidental shutdown when starting the app
        // during daytime or late morning
        var hkNow = now.InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
        if (IsTimeReached(hkNow.TimeOfDay, timing.HardShutdownTime))
        {
            context.State = BehaviorState.Disabled;
            LoggerService.Info("Orchestrator: 當前時間已過硬關機時間, 狀態=Disabled");
        }

        lock (_lock)
        {
            _context = context;
        }

        LoggerService.Info($"Orchestrator: 初始化完成, 狀態={context.State}");
    }

    private void StartTimer()
    {
        lock (_lock)
        {
            _timer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    private async Task OnTimerTickAsync()
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null)
            return;

        var now = _clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
        var currentTime = now.TimeOfDay;

        // Check for cycle transition (12:00 boundary)
        var currentCycle = SleepCycleCalculator.GetCurrentCycle(now);
        if (!SleepCycleCalculator.IsSameCycle(ctx.CurrentCycle.CycleId, currentCycle.CycleId))
        {
            // New cycle started - reinitialize
            // But don't interrupt if shutdown is in progress
            if (ctx.State != BehaviorState.ShuttingDown && ctx.State != BehaviorState.AIMode)
            {
                await InitializeContextAsync(""); // TODO: Store calendar URL
                return;
            }
        }

        // State machine
        switch (ctx.State)
        {
            case BehaviorState.Idle:
                await HandleIdleStateAsync(ctx, currentTime);
                break;

            case BehaviorState.PreWarning:
                await HandlePreWarningStateAsync(ctx, currentTime);
                break;

            case BehaviorState.Warning:
                await HandleWarningStateAsync(ctx, currentTime, now);
                break;

            case BehaviorState.Delayed:
                await HandleDelayedStateAsync(ctx, now);
                break;

            case BehaviorState.AutoDelaying:
                await HandleAutoDelayingStateAsync(ctx, now);
                break;

            case BehaviorState.AIMode:
                // Waiting for AI completion callback
                break;

            case BehaviorState.ShuttingDown:
                // Waiting for shutdown
                break;

            case BehaviorState.Disabled:
                // Do nothing
                break;
        }
    }

    private async Task HandleIdleStateAsync(BehaviorContext ctx, LocalTime currentTime)
    {
        // Check if we've reached pre-warning time
        if (IsTimeReached(currentTime, ctx.Timing.PreWarningTime))
        {
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.PreWarning);
            }

            var timeUntil = CalculateTimeUntil(currentTime, ctx.Timing.WarningTime);
            var firstClassInfo = ctx.Timing.FirstClassTime.HasValue
                ? $"明日早課 {ctx.Timing.FirstClassTime.Value.ToString("HH:mm", null)}"
                : "明日無早課";

            await _notificationService.ShowPreWarningAsync(timeUntil, firstClassInfo);
        }
    }

    private async Task HandlePreWarningStateAsync(BehaviorContext ctx, LocalTime currentTime)
    {
        // Check if we've reached main warning time
        if (IsTimeReached(currentTime, ctx.Timing.WarningTime))
        {
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.Warning);
            }

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = "睡眠週期警告",
                Message = GetWarningMessage(ctx),
                QuotaRemaining = ctx.CurrentCycle.QuotaRemaining,
                IsUnlimitedDelay = ctx.Timing.IsUnlimitedManualDelay,
                ShowDelayButton = true,
                ShowShutdownButton = true,
                ShowAIModeButton = true
            });
        }
    }

    private async Task HandleWarningStateAsync(BehaviorContext ctx, LocalTime currentTime, ZonedDateTime now)
    {
        // If user doesn't respond, automatically delay (silent auto-delay)
        var warningDuration = now - (ctx.NextActionTime ?? now);

        if (warningDuration > Duration.FromMinutes(2))
        {
            // User didn't respond - auto delay
            if (!ctx.Timing.IsUnlimitedManualDelay && ctx.CurrentCycle.QuotaRemaining < ctx.DelayIncrement)
            {
                // Quota exhausted - shutdown now
                lock (_lock)
                {
                    ChangeState(ctx, BehaviorState.ShuttingDown);
                }

                await _shutdownService.InitiateShutdownAsync("Quota exhausted, no response", _cts?.Token ?? default);
                return;
            }

            ctx.CurrentCycle.AutoDelayCount++;
            ctx.NextActionTime = now.Plus(Duration.FromTimeSpan(ctx.DelayIncrement));

            if (!ctx.Timing.IsUnlimitedManualDelay)
            {
                ctx.CurrentCycle.QuotaRemaining -= ctx.DelayIncrement;
            }

            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.AutoDelaying);
            }
            SaveCycleState();
        }

        // Check if hard shutdown time reached
        if (IsTimeReached(currentTime, ctx.Timing.HardShutdownTime))
        {
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.ShuttingDown);
            }

            await _shutdownService.InitiateShutdownAsync("Hard shutdown time reached", _cts?.Token ?? default);
        }
    }

    private async Task HandleDelayedStateAsync(BehaviorContext ctx, ZonedDateTime now)
    {
        // Check if delay period expired
        if (ctx.NextActionTime.HasValue && now.ToInstant() >= ctx.NextActionTime.Value.ToInstant())
        {
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.Warning);
            }

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = "睡眠週期警告",
                Message = GetWarningMessage(ctx),
                QuotaRemaining = ctx.CurrentCycle.QuotaRemaining,
                IsUnlimitedDelay = ctx.Timing.IsUnlimitedManualDelay,
                ShowDelayButton = ctx.Timing.IsUnlimitedManualDelay || ctx.CurrentCycle.QuotaRemaining >= ctx.DelayIncrement,
                ShowShutdownButton = true,
                ShowAIModeButton = true
            });
        }
    }

    private async Task HandleAutoDelayingStateAsync(BehaviorContext ctx, ZonedDateTime now)
    {
        // Check if auto-delay period expired
        if (ctx.NextActionTime.HasValue && now.ToInstant() >= ctx.NextActionTime.Value.ToInstant())
        {
            // Check if we've reached hard shutdown time
            if (IsTimeReached(now.TimeOfDay, ctx.Timing.HardShutdownTime))
            {
                lock (_lock)
                {
                    ChangeState(ctx, BehaviorState.ShuttingDown);
                }

                await _shutdownService.InitiateShutdownAsync("Hard shutdown after auto-delays", _cts?.Token ?? default);
                return;
            }

            // Otherwise return to warning state
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.Warning);
            }

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = "睡眠週期警告",
                Message = GetWarningMessage(ctx),
                QuotaRemaining = ctx.CurrentCycle.QuotaRemaining,
                IsUnlimitedDelay = ctx.Timing.IsUnlimitedManualDelay,
                ShowDelayButton = ctx.Timing.IsUnlimitedManualDelay || ctx.CurrentCycle.QuotaRemaining >= ctx.DelayIncrement,
                ShowShutdownButton = true,
                ShowAIModeButton = true
            });
        }
    }

    private void ChangeState(BehaviorContext ctx, BehaviorState newState)
    {
        var oldState = ctx.State;
        ctx.State = newState;

        StateChanged?.Invoke(this, new BehaviorStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState,
            CurrentCycle = ctx.CurrentCycle,
            Timing = ctx.Timing
        });
    }

    /// <summary>
    /// Checks if current time has reached the target time.
    /// IMPORTANT: Only returns true during the sleep window (21:30 ~ next day 12:00).
    /// This prevents false positives during daytime hours.
    /// </summary>
    private bool IsTimeReached(LocalTime current, LocalTime target)
    {
        var currentMinutes = current.Hour * 60 + current.Minute;
        var targetMinutes = target.Hour * 60 + target.Minute;

        // Sleep window boundaries
        const int sleepStartMinutes = 21 * 60 + 30; // 21:30
        const int sleepEndMinutes = 12 * 60;        // 12:00 (noon)

        // First, check if we're inside the sleep window
        bool isInSleepWindow = currentMinutes >= sleepStartMinutes || currentMinutes < sleepEndMinutes;

        if (!isInSleepWindow)
        {
            // Daytime (12:00 ~ 21:30) - no shutdown/warning should ever trigger
            return false;
        }

        // We're in the sleep window. Now check if we've reached the target time.
        // Target times are always within [21:30, 03:00] range.

        if (targetMinutes >= sleepStartMinutes)
        {
            // Target is in evening (21:30 ~ 23:59)
            // We've reached it if current is also in evening and >= target
            return currentMinutes >= targetMinutes && currentMinutes >= sleepStartMinutes;
        }
        else
        {
            // Target is after midnight (00:00 ~ 03:00)
            // We've reached it if current is after midnight and >= target
            return currentMinutes >= targetMinutes && currentMinutes < sleepEndMinutes;
        }
    }

    private TimeSpan CalculateTimeUntil(LocalTime from, LocalTime to)
    {
        var fromMinutes = from.Hour * 60 + from.Minute;
        var toMinutes = to.Hour * 60 + to.Minute;

        if (toMinutes > fromMinutes)
        {
            return TimeSpan.FromMinutes(toMinutes - fromMinutes);
        }
        else
        {
            return TimeSpan.FromMinutes((24 * 60 - fromMinutes) + toMinutes);
        }
    }

    private string GetWarningMessage(BehaviorContext ctx)
    {
        if (ctx.Timing.Classification == DayClassification.EarlyClass)
        {
            return $"明日早課 {ctx.Timing.FirstClassTime?.ToString("HH:mm", null) ?? "未知"}。" +
                   $"剩餘額度：{ctx.CurrentCycle.QuotaRemaining.TotalMinutes:F0} 分鐘。";
        }
        else
        {
            return "明日無早課，手動延後無限制。";
        }
    }

    public BehaviorContext? GetCurrentContext()
    {
        lock (_lock)
        {
            return _context;
        }
    }

    // ── 配額持久化（重啟不重置） ──

    private static readonly string _registryPath = @"SOFTWARE\Terminus";

    /// <summary>
    /// 將當前週期的配額/延遲次數存到 registry，重啟後可恢復。
    /// </summary>
    private void SaveCycleState()
    {
        try
        {
            var ctx = _context;
            if (ctx == null) return;
            var c = ctx.CurrentCycle;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("CycleId", c.CycleId);
            key.SetValue("QuotaRemainingMinutes", (int)c.QuotaRemaining.TotalMinutes);
            key.SetValue("DelayCount", c.DelayCount);
            key.SetValue("AutoDelayCount", c.AutoDelayCount);
        }
        catch { }
    }

    /// <summary>
    /// 若 registry 中存的是同一個週期，恢復配額/延遲次數。
    /// </summary>
    private void TryRestoreCycleState(SleepCycle cycle)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_registryPath);
            if (key == null) return;

            var savedId = key.GetValue("CycleId") as string;
            if (savedId != cycle.CycleId) return;

            var qMin = key.GetValue("QuotaRemainingMinutes") as int?;
            var dc = key.GetValue("DelayCount") as int?;
            var adc = key.GetValue("AutoDelayCount") as int?;

            if (qMin.HasValue) cycle.QuotaRemaining = TimeSpan.FromMinutes(qMin.Value);
            if (dc.HasValue) cycle.DelayCount = dc.Value;
            if (adc.HasValue) cycle.AutoDelayCount = adc.Value;

            LoggerService.Info($"Orchestrator: 恢復週期狀態, Quota={cycle.QuotaRemaining.TotalMinutes:F0}min, DelayCount={cycle.DelayCount}, AutoDelayCount={cycle.AutoDelayCount}");
        }
        catch { }
    }
}
