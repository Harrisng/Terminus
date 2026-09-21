using FluentAssertions;
using NodaTime;
using Terminus.Core.Logic;
using Terminus.Core.Models;
using Terminus.Core.Services;
using Xunit;

namespace Terminus.Core.Tests.Services;

/// <summary>
/// BehaviorOrchestrator 狀態機測試。
/// 涵蓋：手動延遲（含配額檢查）、立即關機、AI 模式進入/完成、AutoDelayUsed 重置規則。
/// </summary>
public class BehaviorOrchestratorTests
{
    private static readonly DateTimeZone HongKongTimeZone = DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"];

    private readonly StubClock _clock;
    private readonly RecordingNotificationService _notifications = new();
    private readonly StubCycleStateService _cycleState = new();
    private readonly SpyShutdownService _shutdown;

    public BehaviorOrchestratorTests()
    {
        // 預設時間：2026-06-17 23:00 HK（在警告時間之前的 Idle 階段）
        _clock = new StubClock(Instant.FromUtc(2026, 6, 17, 15, 0));
        _shutdown = new SpyShutdownService(_clock);
    }

    /// <summary>建立 orchestrator 並注入 stub 服務。caller 可接著設定 context。</summary>
    private BehaviorOrchestrator CreateOrchestrator()
    {
        var calSvc = new CalendarDataService(new StubICalService(), new CacheService());
        var orch = new BehaviorOrchestrator(calSvc, _shutdown, _notifications, _cycleState, new NullLocalizationService(), _clock);
        return orch;
    }

    /// <summary>建立一個早課日 context（90 分鐘配額）並放到 orchestrator。</summary>
    private static BehaviorContext CreateEarlyClassContext(BehaviorOrchestrator orch, BehaviorState state)
    {
        var now = orch.GetCurrentHkNow();
        var cycle = new SleepCycle
        {
            CycleId = "2026-06-17T12:00",
            StartTime = now,
            TargetDate = new LocalDate(2026, 6, 18),
            State = SleepCycleState.Idle,
            QuotaRemaining = TimeSpan.FromMinutes(90),
            TotalQuota = TimeSpan.FromMinutes(90),
            DelayCount = 0,
            AutoDelayCount = 0
        };
        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            new LocalTime(9, 0));
        var ctx = new BehaviorContext
        {
            CurrentCycle = cycle,
            Timing = timing,
            State = state,
            NextActionTime = null
        };
        orch.SetContextForTesting(ctx);
        return ctx;
    }

    // ── OnDelayRequestedAsync ──

    [Fact]
    public async Task OnDelayRequestedAsync_WithSufficientQuota_SucceedsAndReducesQuota()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);

        var result = await orch.OnDelayRequestedAsync(TimeSpan.FromMinutes(30));

        result.Should().BeTrue();
        ctx.State.Should().Be(BehaviorState.Delayed);
        ctx.CurrentCycle.QuotaRemaining.TotalMinutes.Should().Be(60);
        ctx.CurrentCycle.DelayCount.Should().Be(1);
        ctx.NextActionTime.Should().NotBeNull();
        _notifications.LastDelayConfirmation.Should().NotBeNull();
        _cycleState.LastSavedType.Should().Be("manual");
    }

    [Fact]
    public async Task OnDelayRequestedAsync_WithInsufficientQuota_FailsAndKeepsQuota()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);
        ctx.CurrentCycle.QuotaRemaining = TimeSpan.FromMinutes(15);

        var result = await orch.OnDelayRequestedAsync(TimeSpan.FromMinutes(30));

        result.Should().BeFalse();
        ctx.State.Should().Be(BehaviorState.Warning);   // 狀態不變
        ctx.CurrentCycle.QuotaRemaining.TotalMinutes.Should().Be(15);
        ctx.CurrentCycle.DelayCount.Should().Be(0);
        _notifications.LastWarningTitle.Should().Be("配額已用完");
    }

    [Fact]
    public async Task OnDelayRequestedAsync_NotInWarningState_ReturnsFalse()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Idle);

        var result = await orch.OnDelayRequestedAsync(TimeSpan.FromMinutes(30));

        result.Should().BeFalse();
        ctx.State.Should().Be(BehaviorState.Idle);
        ctx.CurrentCycle.DelayCount.Should().Be(0);
    }

    [Fact]
    public async Task OnDelayRequestedAsync_OnNonEarlyClassDay_DoesNotConsumeQuota()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);
        ctx.Timing = TimingCalculator.CalculateTiming(
            DayClassification.NonEarlyClass, null);

        var result = await orch.OnDelayRequestedAsync(TimeSpan.FromMinutes(60));

        result.Should().BeTrue();
        ctx.State.Should().Be(BehaviorState.Delayed);
        ctx.CurrentCycle.QuotaRemaining.TotalMinutes.Should().Be(90);   // 沒扣
        ctx.CurrentCycle.DelayCount.Should().Be(1);
    }

    // ── OnShutdownNowRequestedAsync ──

    [Fact]
    public async Task OnShutdownNowRequestedAsync_TransitionsToShuttingDownAndCallsShutdownService()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);

        await orch.OnShutdownNowRequestedAsync();

        ctx.State.Should().Be(BehaviorState.ShuttingDown);
        _shutdown.InitiateCalled.Should().BeTrue();
        _shutdown.LastReason.Should().Contain("User requested");
    }

    // ── OnAIModeRequestedAsync / OnAIModeCompletedAsync ──

    [Fact]
    public async Task OnAIModeRequestedAsync_TransitionsToAIModeAndStoresDescription()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);

        await orch.OnAIModeRequestedAsync("整理筆記");

        ctx.State.Should().Be(BehaviorState.AIMode);
        ctx.IsAIModeActive.Should().BeTrue();
        ctx.AITaskDescription.Should().Be("整理筆記");
    }

    [Fact]
    public async Task OnAIModeCompletedAsync_TransitionsToShuttingDownAndClearsFlag()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.AIMode);
        ctx.IsAIModeActive = true;
        ctx.AITaskDescription = "整理筆記";

        await orch.OnAIModeCompletedAsync(success: true);

        ctx.State.Should().Be(BehaviorState.ShuttingDown);
        ctx.IsAIModeActive.Should().BeFalse();
        ctx.AITaskDescription.Should().BeNull();
        _shutdown.InitiateCalled.Should().BeTrue();
        _shutdown.LastReason.Should().Contain("AI mode completed");
    }

    [Fact]
    public async Task OnAIModeCompletedAsync_WhenAIModeNotActive_DoesNothing()
    {
        var orch = CreateOrchestrator();
        var ctx = CreateEarlyClassContext(orch, BehaviorState.Warning);  // 沒進入 AI 模式
        ctx.IsAIModeActive = false;

        await orch.OnAIModeCompletedAsync(success: true);

        ctx.State.Should().Be(BehaviorState.Warning);  // 不變
        _shutdown.InitiateCalled.Should().BeFalse();
    }
}

// ── 內部測試 stubs / spies ──

internal sealed class StubCycleStateService : ICycleStateService
{
    public string? LastSavedType { get; private set; }
    public int SaveCallCount { get; private set; }

    public void SaveCycleState(BehaviorContext context, string? delayType = null, int delayMinutes = 0, string? delayedTo = null)
    {
        SaveCallCount++;
        LastSavedType = delayType;
    }

    public void TryRestoreCycleState(SleepCycle cycle) { }
    public void RestoreContextState(BehaviorContext context, ZonedDateTime hkNow) { }
    public CycleStateData? ReadState() => null;
    public void WriteState(CycleStateData state) { }
}

internal sealed class SpyShutdownService : ShutdownService
{
    public bool InitiateCalled { get; private set; }
    public string? LastReason { get; private set; }

    public SpyShutdownService(IClock clock) : base(clock) { }

    public override Task<ShutdownResult> InitiateShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        InitiateCalled = true;
        LastReason = reason;
        return Task.FromResult(new ShutdownResult
        {
            Success = true,
            Message = "stub",
            WasCancelled = false
        });
    }
}

internal sealed class StubICalService : ICalService
{
    public StubICalService() : base(new HttpClient()) { }

    public new async Task<List<CalendarEvent>> FetchCalendarAsync(string url) => await Task.FromResult(new List<CalendarEvent>());
}

internal sealed class StubClock : IClock
{
    private Instant _now;
    public StubClock(Instant initial) { _now = initial; }
    public Instant GetCurrentInstant() => _now;
    public void Advance(Duration d) => _now = _now + d;
}

/// <summary>記錄所有通知呼叫的 INotificationService spy，供測試斷言。</summary>
internal sealed class RecordingNotificationService : INotificationService
{
    public TimeSpan? LastDelayConfirmation { get; private set; }
    public string? LastWarningTitle { get; private set; }
    public WarningNotificationArgs? LastWarningArgs { get; private set; }
    public string? LastAIModeStarted { get; private set; }
    public (string desc, bool success)? LastAIModeCompleted { get; private set; }
    public TimeSpan? LastShutdownImminent { get; private set; }

    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo) => Task.CompletedTask;
    public Task ShowWarningAsync(WarningNotificationArgs args) { LastWarningArgs = args; LastWarningTitle = args.Title; return Task.CompletedTask; }
    public Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited) { LastDelayConfirmation = delayedUntil; return Task.CompletedTask; }
    public Task ShowAIModeStartedAsync(string taskDescription) { LastAIModeStarted = taskDescription; return Task.CompletedTask; }
    public Task ShowAIModeCompletedAsync(string taskDescription, bool success) { LastAIModeCompleted = (taskDescription, success); return Task.CompletedTask; }
    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining) { LastShutdownImminent = timeRemaining; return Task.CompletedTask; }
    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours) => Task.CompletedTask;
    public Task ClearAllNotificationsAsync() => Task.CompletedTask;
}
