using NodaTime;

namespace Terminus.Core.Models;

/// <summary>
/// Calculated timing for a sleep cycle based on day classification and settings.
/// </summary>
public class ScheduleTiming
{
    /// <summary>
    /// Day classification (early-class, non-early-class, or no data)
    /// </summary>
    public required DayClassification Classification { get; init; }

    /// <summary>
    /// Pre-warning time (typically 30 minutes before main warning)
    /// </summary>
    public required LocalTime PreWarningTime { get; init; }

    /// <summary>
    /// Main warning time (when user gets notified with action buttons)
    /// </summary>
    public required LocalTime WarningTime { get; init; }

    /// <summary>
    /// Hard shutdown time (when quota is exhausted or silent auto-delay limit reached)
    /// </summary>
    public required LocalTime HardShutdownTime { get; init; }

    /// <summary>
    /// Time of the first morning class (null if non-early-class or no data)
    /// </summary>
    public LocalTime? FirstClassTime { get; init; }

    /// <summary>
    /// Total delay quota available (typically 90 minutes for early-class, unlimited for non-early-class manual delays)
    /// </summary>
    public TimeSpan TotalQuota { get; init; }

    /// <summary>
    /// Whether manual delays are unlimited (true for non-early-class days)
    /// </summary>
    public bool IsUnlimitedManualDelay { get; init; }

    /// <summary>
    /// Whether minimum warning insurance was applied (warning pushed earlier to maintain 30min gap)
    /// </summary>
    public bool MinimumWarningInsuranceApplied { get; init; }

    /// <summary>
    /// Original calculated warning time before insurance adjustment (if any)
    /// </summary>
    public LocalTime? OriginalWarningTime { get; init; }
}
