using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Models;

/// <summary>
/// Current state of the behavior orchestrator.
/// </summary>
public enum BehaviorState
{
    /// <summary>
    /// Idle, waiting for warning time
    /// </summary>
    Idle,

    /// <summary>
    /// Pre-warning phase (30 minutes before main warning)
    /// </summary>
    PreWarning,

    /// <summary>
    /// Main warning active, showing notification with buttons
    /// </summary>
    Warning,

    /// <summary>
    /// User pressed delay, waiting for delay period to expire
    /// </summary>
    Delayed,

    /// <summary>
    /// Silent auto-delay (user didn't respond)
    /// </summary>
    AutoDelaying,

    /// <summary>
    /// Shutdown in progress
    /// </summary>
    ShuttingDown,

    /// <summary>
    /// AI overnight mode active
    /// </summary>
    AIMode,

    /// <summary>
    /// Cycle disabled (reboot after shutdown)
    /// </summary>
    Disabled
}

/// <summary>
/// Event raised when behavior state changes.
/// </summary>
public class BehaviorStateChangedEventArgs : EventArgs
{
    public required BehaviorState OldState { get; init; }
    public required BehaviorState NewState { get; init; }
    public required SleepCycle CurrentCycle { get; init; }
    public required ScheduleTiming Timing { get; init; }
}

/// <summary>
/// Context for behavior orchestrator.
/// </summary>
public class BehaviorContext
{
    public required SleepCycle CurrentCycle { get; set; }
    public required ScheduleTiming Timing { get; set; }
    public required BehaviorState State { get; set; }
    public ZonedDateTime? NextActionTime { get; set; }
    public TimeSpan DelayIncrement { get; set; } = TimeSpan.FromMinutes(30);
    public bool IsAIModeActive { get; set; }
    public string? AITaskDescription { get; set; }
    /// <summary>本輪警告是否已用過一次自動延遲。用過後再無回應就直接關機。</summary>
    public bool AutoDelayUsed { get; set; }
    public List<CalendarEvent> Events { get; set; } = new();
}
