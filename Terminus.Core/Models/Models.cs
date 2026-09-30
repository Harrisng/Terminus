using NodaTime;

namespace Terminus.Core.Models;

// ════════════════════════════════════════════════════════════
// 日分類
// ════════════════════════════════════════════════════════════

/// <summary>
/// Classification of a target day based on morning class schedule.
/// </summary>
public enum DayClassification
{
    /// <summary>
    /// Has qualifying course(s) between 00:00-12:00 HK time (strict: before 12:00, not at 12:00)
    /// </summary>
    EarlyClass,

    /// <summary>
    /// No morning classes (includes: no classes at all, afternoon-only, weekend, holiday, class exactly at 12:00)
    /// </summary>
    NonEarlyClass,

    /// <summary>
    /// No schedule data available (network error, no cache)
    /// </summary>
    NoData
}

// ════════════════════════════════════════════════════════════
// 日曆事件與假期
// ════════════════════════════════════════════════════════════

/// <summary>
/// Represents a parsed calendar event (course class).
/// </summary>
public class CalendarEvent
{
    /// <summary>
    /// Event summary/title (e.g., "ITP4416")
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Start time in Hong Kong timezone
    /// </summary>
    public required ZonedDateTime Start { get; init; }

    /// <summary>
    /// End time in Hong Kong timezone
    /// </summary>
    public required ZonedDateTime End { get; init; }

    /// <summary>
    /// Whether this is an all-day event (VALUE=DATE)
    /// </summary>
    public bool IsAllDay { get; init; }

    /// <summary>
    /// Original event UID for tracking
    /// </summary>
    public string? Uid { get; init; }

    /// <summary>
    /// Location/room if specified
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Whether this event qualifies as a course (matches pattern and criteria)
    /// </summary>
    public bool IsQualifyingCourse { get; init; }
}

/// <summary>
/// Represents a Hong Kong public holiday.
/// </summary>
public class PublicHoliday
{
    /// <summary>
    /// Holiday name (e.g., "中秋節翌日")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Holiday date (whole day)
    /// </summary>
    public required LocalDate Date { get; init; }

    /// <summary>
    /// Original event UID
    /// </summary>
    public string? Uid { get; init; }
}

// ════════════════════════════════════════════════════════════
// 快取相關
// ════════════════════════════════════════════════════════════

/// <summary>
/// Cache status for calendar data.
/// </summary>
public enum CacheStatus
{
    /// <summary>
    /// Data is fresh from the network
    /// </summary>
    Fresh,

    /// <summary>
    /// Using cached data due to network failure
    /// </summary>
    Cached,

    /// <summary>
    /// No cache available, using default (lenient/conservative mode)
    /// </summary>
    Default,

    /// <summary>
    /// Cache file was corrupted and has been renamed
    /// </summary>
    Corrupted
}

/// <summary>
/// Metadata for cached calendar data.
/// </summary>
public class CacheMetadata
{
    /// <summary>
    /// When this data was fetched
    /// </summary>
    public required DateTime FetchedAt { get; init; }

    /// <summary>
    /// Source URL that was fetched
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Whether the fetch was successful
    /// </summary>
    public bool FetchSuccess { get; init; }

    /// <summary>
    /// Error message if fetch failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of events cached
    /// </summary>
    public int EventCount { get; init; }
}

/// <summary>
/// Cached calendar data with metadata.
/// </summary>
public class CachedCalendarData<T>
{
    /// <summary>
    /// The cached data
    /// </summary>
    public required List<T> Data { get; init; }

    /// <summary>
    /// Cache metadata
    /// </summary>
    public required CacheMetadata Metadata { get; init; }
}

// ════════════════════════════════════════════════════════════
// 睡眠週期
// ════════════════════════════════════════════════════════════

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

// ════════════════════════════════════════════════════════════
// 週期狀態持久化
// ════════════════════════════════════════════════════════════

/// <summary>
/// 週期狀態資料（DPAPI 加密存檔用）。
/// </summary>
public class CycleStateData
{
    public string CycleId { get; set; } = "";
    public int QuotaRemainingMinutes { get; set; }
    public int DelayCount { get; set; }
    public int AutoDelayCount { get; set; }
    public string LastUpdated { get; set; } = "";
    public List<DelayRecord> History { get; set; } = new();
    /// <summary>當前狀態（重啟還原用）</summary>
    public string? State { get; set; }
    /// <summary>下次動作時間 ISO 格式（重啟還原用）</summary>
    public string? NextActionTime { get; set; }
    /// <summary>是否已用過自動延遲（重啟還原用，防止重啟後重複自動延遲）</summary>
    public bool AutoDelayUsed { get; set; }
}

/// <summary>
/// 延遲歷史記錄。
/// </summary>
public class DelayRecord
{
    /// <summary>點擊時間</summary>
    public string Timestamp { get; set; } = "";
    /// <summary>延遲分鐘數</summary>
    public int DurationMinutes { get; set; }
    /// <summary>延後到幾點幾分</summary>
    public string DelayedTo { get; set; } = "";
    /// <summary>"manual" or "auto"</summary>
    public string Type { get; set; } = "";
}

// ════════════════════════════════════════════════════════════
// 排程時間計算結果
// ════════════════════════════════════════════════════════════

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

    /// <summary>
    /// Whether the target date is a Hong Kong public holiday.
    /// Holiday days are treated as NonEarlyClass timing-wise but displayed distinctly.
    /// </summary>
    public bool IsHoliday { get; init; }
}

// ════════════════════════════════════════════════════════════
// 行為狀態與上下文
// ════════════════════════════════════════════════════════════

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
    /// Forced shutdown warning: hard shutdown time reached, giving 5 min to save data before actual shutdown.
    /// Cannot be delayed or dismissed.
    /// </summary>
    ForceShutdown,

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
    /// <summary>香港公眾假期列表（供 ScheduleClassifier 與 UI 使用，可能為 null 表示尚未抓取）。</summary>
    public List<PublicHoliday>? Holidays { get; set; }
}
