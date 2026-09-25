using System.Diagnostics;
using System.Text;
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
    // ── 延遲時長常數（避免散落的 magic number） ──
    /// <summary>延遲選項：30 分鐘</summary>
    public static readonly TimeSpan DelayOptionShort = TimeSpan.FromMinutes(30);
    /// <summary>延遲選項：60 分鐘</summary>
    public static readonly TimeSpan DelayOptionMedium = TimeSpan.FromMinutes(60);
    /// <summary>延遲選項：90 分鐘</summary>
    public static readonly TimeSpan DelayOptionLong = TimeSpan.FromMinutes(90);

    /// <summary>預設警告無回應寬限期：5 分鐘。可由 <see cref="WarningGracePeriod"/> 覆寫。</summary>
    public static readonly Duration DefaultWarningGracePeriod = Duration.FromMinutes(5);

    /// <summary>預設硬關機強制提醒寬限期：5 分鐘。可由 <see cref="ForceShutdownGracePeriod"/> 覆寫。</summary>
    public static readonly Duration DefaultForceShutdownGracePeriod = Duration.FromMinutes(5);

    /// <summary>警告無回應寬限期。超過後自動延遲一次或直接關機。由 App.ApplyOrchestratorSettings 設定。</summary>
    public Duration WarningGracePeriod { get; set; } = DefaultWarningGracePeriod;

    /// <summary>硬關機強制提醒寬限期。到達硬關機時間後先給用戶多久儲存資料再關機。由 App.ApplyOrchestratorSettings 設定。</summary>
    public Duration ForceShutdownGracePeriod { get; set; } = DefaultForceShutdownGracePeriod;

    private readonly CalendarDataService _calendarService;
    private readonly ShutdownService _shutdownService;
    private readonly INotificationService _notificationService;
    private readonly ICycleStateService _cycleStateService;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;
    private readonly object _lock = new();

    private BehaviorContext? _context;
    private Timer? _timer;
    private CancellationTokenSource? _cts;
    /// <summary>最近一次啟動時使用的日曆 URL，跨週期重新初始化時重複使用。</summary>
    private string? _lastCalendarUrl;

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
    public TimeSpan EarlyClassDelayQuota { get; set; } = DelayOptionLong;

    public event EventHandler<BehaviorStateChangedEventArgs>? StateChanged;

    public BehaviorOrchestrator(
        CalendarDataService calendarService,
        ShutdownService shutdownService,
        INotificationService notificationService,
        ICycleStateService cycleStateService,
        ILocalizationService localization,
        IClock? clock = null)
    {
        _calendarService = calendarService;
        _shutdownService = shutdownService;
        _notificationService = notificationService;
        _cycleStateService = cycleStateService;
        _localization = localization;
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

        _lastCalendarUrl = calendarUrl;
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
        _lastCalendarUrl = calendarUrl;
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
                    Title = _localization.Get("Warning_QuotaExceededTitle"),
                    Message = _localization.Get("Warning_QuotaExceededBody",
                        ctx.CurrentCycle.QuotaRemaining.TotalMinutes, delayAmount.TotalMinutes),
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

        var delayedTo = ctx.NextActionTime?.ToDateTimeUnspecified().ToString("yyyy-MM-dd HH:mm");
        _cycleStateService.SaveCycleState(ctx, "manual", (int)delayAmount.TotalMinutes, delayedTo);

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
    /// User pressed AI overnight mode button. Starts the external process and
    /// transitions to AIMode state. When the process exits, OnAIModeCompletedAsync
    /// is invoked automatically (success = exit code 0).
    /// </summary>
    public async Task OnAIModeRequestedAsync(string commandLine, string workingDirectory, string description)
    {
        BehaviorContext? ctx;
        lock (_lock)
        {
            ctx = _context;
        }

        if (ctx == null)
            return;

        ctx.IsAIModeActive = true;
        ctx.AITaskDescription = description;

        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.AIMode);
        }

        await _notificationService.ShowAIModeStartedAsync(description);

        // Fire-and-forget process monitoring; completion callback handles state transition
        _ = MonitorAIProcessAsync(commandLine, workingDirectory, description);
    }

    /// <summary>
    /// Monitor the external process. On exit, invokes OnAIModeCompletedAsync.
    /// On failure to start, immediately completes with success=false (no shutdown).
    /// </summary>
    private async Task MonitorAIProcessAsync(string commandLine, string workingDirectory, string description)
    {
        try
        {
            var (fileName, args) = ParseCommandLine(commandLine);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = false
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                psi.WorkingDirectory = workingDirectory;
            foreach (var arg in SplitArguments(args))
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                LoggerService.Error($"AIMode: 無法啟動進程: {commandLine}");
                await OnAIModeCompletedAsync(false);
                return;
            }

            // Read stderr for diagnostic on failure
            var stderrBuilder = new StringBuilder();
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    stderrBuilder.AppendLine(e.Data);
            };
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();

            var success = proc.ExitCode == 0;
            if (!success)
                LoggerService.Warn($"AIMode: 進程退出碼 {proc.ExitCode} ({description})\nStderr:\n{stderrBuilder}");

            await OnAIModeCompletedAsync(success);
        }
        catch (Exception ex)
        {
            LoggerService.Error($"AIMode: 啟動進程失敗 ({commandLine})", ex);
            await OnAIModeCompletedAsync(false);
        }
    }

    /// <summary>
    /// Split a command line into (fileName, arguments). Handles quoted first token.
    /// </summary>
    private static (string fileName, string args) ParseCommandLine(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.Length == 0)
            return (string.Empty, string.Empty);

        if (trimmed[0] == '"' || trimmed[0] == '\'')
        {
            var quote = trimmed[0];
            var end = trimmed.IndexOf(quote, 1);
            if (end > 0)
                return (trimmed.Substring(1, end - 1), trimmed.Substring(end + 1).TrimStart());
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx < 0)
            return (trimmed, string.Empty);
        return (trimmed.Substring(0, spaceIdx), trimmed.Substring(spaceIdx + 1).TrimStart());
    }

    /// <summary>
    /// Split arguments string respecting double-quotes (Windows-style).
    /// Returns only the arguments portion (fileName already excluded).
    /// </summary>
    private static IEnumerable<string> SplitArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            yield break;

        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in args)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }

    /// <summary>
    /// AI mode completed callback. success=true → proceed to shutdown.
    /// success=false → return to Idle (let user inspect error).
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

        if (success)
        {
            // Proceed to shutdown
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.ShuttingDown);
            }

            await _shutdownService.InitiateShutdownAsync("AI mode completed", _cts?.Token ?? default);
        }
        else
        {
            // Failed: return to Idle so user can inspect the error
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.Idle);
            }
        }
    }

    private async Task InitializeContextAsync(string calendarUrl)
    {
        var now = _clock.GetCurrentInstant();
        var cycle = SleepCycleCalculator.GetCurrentCycle(now);
        _cycleStateService.TryRestoreCycleState(cycle);

        var safeUrl = calendarUrl ?? string.Empty;
        LoggerService.Info($"Orchestrator: InitializeContextAsync 開始, URL={safeUrl.Substring(0, Math.Min(50, safeUrl.Length))}...");

        // Fetch schedule data
        var (events, status, cacheAge) = await _calendarService.GetScheduleAsync(safeUrl);
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

        // 還原上次存檔的狀態和下次動作時間（重啟不丟失延遲狀態）
        if (context.State != BehaviorState.Disabled)
        {
            _cycleStateService.RestoreContextState(context, hkNow);
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
                await InitializeContextAsync(_lastCalendarUrl ?? string.Empty);
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

            case BehaviorState.ForceShutdown:
                await HandleForceShutdownStateAsync(ctx, now);
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
                ? _localization.Get("Warning_TomorrowEarlyClassFormat", ctx.Timing.FirstClassTime.Value.ToString("HH:mm", null))
                : _localization.Get("Warning_NoEarlyClass");

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

            // 設定 NextActionTime 為目前時間，啟動 5 分鐘無回應自動延遲倒數
            // 新一輪警告開始，重置自動延遲使用記錄
            var now = _clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
            ctx.NextActionTime = now;
            ctx.AutoDelayUsed = false;

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = _localization.Get("Notification_WarningTitle"),
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
        // 無回應寬限期（5 分鐘）
        var warningDuration = now - (ctx.NextActionTime ?? now);

        if (warningDuration > WarningGracePeriod)
        {
            // 已用過一次自動延遲，仍無回應 → 直接關機
            if (ctx.AutoDelayUsed)
            {
                lock (_lock)
                {
                    ChangeState(ctx, BehaviorState.ShuttingDown);
                }

                await _shutdownService.InitiateShutdownAsync("No response after auto-delay", _cts?.Token ?? default);
                return;
            }

            // 首次無回應：自動延遲一次
            // 早課日配額不足時直接關機，不延遲
            if (!ctx.Timing.IsUnlimitedManualDelay && ctx.CurrentCycle.QuotaRemaining < ctx.DelayIncrement)
            {
                lock (_lock)
                {
                    ChangeState(ctx, BehaviorState.ShuttingDown);
                }

                await _shutdownService.InitiateShutdownAsync("Quota exhausted, no response", _cts?.Token ?? default);
                return;
            }

            ctx.AutoDelayUsed = true;
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
            var autoDelayedTo = ctx.NextActionTime?.ToDateTimeUnspecified().ToString("yyyy-MM-dd HH:mm");
            _cycleStateService.SaveCycleState(ctx, "auto", (int)ctx.DelayIncrement.TotalMinutes, autoDelayedTo);
            return;
        }

        // Check if hard shutdown time reached → 進入強制關機提醒（給 5 分鐘儲存資料）
        if (IsTimeReached(currentTime, ctx.Timing.HardShutdownTime))
        {
            await EnterForceShutdownAsync(ctx, now);
        }
    }

    /// <summary>
    /// 進入強制關機提醒狀態：彈出不可關閉的提醒，5 分鐘後真正關機。
    /// </summary>
    private async Task EnterForceShutdownAsync(BehaviorContext ctx, ZonedDateTime now)
    {
        lock (_lock)
        {
            ChangeState(ctx, BehaviorState.ForceShutdown);
        }

        ctx.NextActionTime = now.Plus(ForceShutdownGracePeriod);

        await _notificationService.ShowShutdownImminentAsync(ForceShutdownGracePeriod.ToTimeSpan());
    }

    /// <summary>
    /// 強制關機提醒狀態：5 分鐘倒數結束後真正關機。
    /// </summary>
    private async Task HandleForceShutdownStateAsync(BehaviorContext ctx, ZonedDateTime now)
    {
        if (ctx.NextActionTime.HasValue && now.ToInstant() >= ctx.NextActionTime.Value.ToInstant())
        {
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.ShuttingDown);
            }

            await _shutdownService.InitiateShutdownAsync("Force shutdown after grace period", _cts?.Token ?? default);
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

            // 手動延遲到期回到 Warning：用戶曾主動操作，重置自動延遲機會
            ctx.NextActionTime = now;
            ctx.AutoDelayUsed = false;

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = _localization.Get("Notification_WarningTitle"),
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
            // Check if we've reached hard shutdown time → 進入強制關機提醒
            if (IsTimeReached(now.TimeOfDay, ctx.Timing.HardShutdownTime))
            {
                await EnterForceShutdownAsync(ctx, now);
                return;
            }

            // Otherwise return to warning state
            lock (_lock)
            {
                ChangeState(ctx, BehaviorState.Warning);
            }

            // 自動延遲到期回到 Warning：AutoDelayUsed 保持 true，
            // 若再無回應（5 分鐘）就直接關機，不再給第二次自動延遲
            ctx.NextActionTime = now;

            await _notificationService.ShowWarningAsync(new WarningNotificationArgs
            {
                Title = _localization.Get("Notification_WarningTitle"),
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
            return _localization.Get("Warning_MessageEarlyClass",
                ctx.Timing.FirstClassTime?.ToString("HH:mm", null) ?? "?",
                ctx.CurrentCycle.QuotaRemaining.TotalMinutes);
        }
        else
        {
            return _localization.Get("Warning_MessageNoEarlyClass");
        }
    }

    public BehaviorContext? GetCurrentContext()
    {
        lock (_lock)
        {
            return _context;
        }
    }

    /// <summary>取得目前 HK 時區的 ZonedDateTime（測試與顯示層共用）。</summary>
    public ZonedDateTime GetCurrentHkNow()
    {
        return _clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
    }

    /// <summary>
    /// 測試專用：直接注入 BehaviorContext，繞過 InitializeContextAsync 與日曆抓取。
    /// 不會啟動計時器。生產環境請改用 StartAsync。
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal void SetContextForTesting(BehaviorContext ctx)
    {
        lock (_lock)
        {
            _context = ctx;
        }
    }
}
