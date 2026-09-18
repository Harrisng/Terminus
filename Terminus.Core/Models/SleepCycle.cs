using NodaTime;

namespace Terminus.Core.Models;

/// <summary>
/// State of the sleep cycle.
/// </summary>
public enum SleepCycleState
{
    /// <summary>
    /// No active warnings or actions
    /// </summary>
    Idle,

    /// <summary>
    /// Pre-warning phase (30 minutes before main warning)
    /// </summary>
    PreWarning,

    /// <summary>
    /// Main warning active (user can delay or shutdown)
    /// </summary>
    Warning,

    /// <summary>
    /// Currently in a delay period
    /// </summary>
    Delayed,

    /// <summary>
    /// AI overnight mode is active
    /// </summary>
    AIMode,

    /// <summary>
    /// This cycle is disabled (user rebooted after shutdown)
    /// </summary>
    Disabled
}

/// <summary>
/// Sleep cycle (就寢週期) - runs from 12:00 one day to 12:00 the next day.
/// Cycle ID format: "2026-06-17T12:00" in HK timezone.
/// </summary>
public class SleepCycle
{
    /// <summary>
    /// Unique cycle identifier in ISO 8601 format (e.g., "2026-06-17T12:00")
    /// This is the cycle's start time at 12:00 noon HK time.
    /// </summary>
    public required string CycleId { get; init; }

    /// <summary>
    /// Cycle start time (12:00 noon HK time)
    /// </summary>
    public required ZonedDateTime StartTime { get; init; }

    /// <summary>
    /// Target date for schedule lookup (the next calendar day)
    /// </summary>
    public required LocalDate TargetDate { get; init; }

    /// <summary>
    /// Current state of this cycle
    /// </summary>
    public SleepCycleState State { get; set; }

    /// <summary>
    /// Remaining quota for manual delays (only applies to early-class days)
    /// </summary>
    public TimeSpan QuotaRemaining { get; set; }

    /// <summary>
    /// Total quota at cycle start (typically 90 minutes for early-class days)
    /// </summary>
    public TimeSpan TotalQuota { get; set; }

    /// <summary>
    /// Number of manual delays triggered this cycle
    /// </summary>
    public int DelayCount { get; set; }

    /// <summary>
    /// Number of automatic (silent) delays triggered this cycle
    /// </summary>
    public int AutoDelayCount { get; set; }
}
