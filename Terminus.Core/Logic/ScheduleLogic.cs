using NodaTime;
using NodaTime.Text;
using Terminus.Core.Models;

namespace Terminus.Core.Logic;

// ════════════════════════════════════════════════════════════
// 睡眠週期計算
// ════════════════════════════════════════════════════════════

/// <summary>
/// Pure functions for sleep cycle calculation.
/// Sleep cycle runs from 12:00 noon one day to 12:00 noon the next day.
/// Cycle ID format: "2026-06-17T12:00" (ISO 8601 in HK timezone).
/// </summary>
public static class SleepCycleCalculator
{
    private static readonly DateTimeZone HongKongTimeZone = DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"];

    /// <summary>
    /// Gets the current sleep cycle for a given moment in time.
    /// Cycle boundaries are at 12:00 noon HK time.
    /// </summary>
    /// <param name="now">Current time (will be converted to HK time)</param>
    /// <param name="totalQuota">Total delay quota for this cycle (default 90 minutes)</param>
    /// <returns>Sleep cycle object with CycleId and TargetDate</returns>
    public static SleepCycle GetCurrentCycle(Instant now, TimeSpan? totalQuota = null)
    {
        var hkNow = now.InZone(HongKongTimeZone);
        return GetCurrentCycle(hkNow, totalQuota);
    }

    /// <summary>
    /// Gets the current sleep cycle for a given HK time.
    /// </summary>
    public static SleepCycle GetCurrentCycle(ZonedDateTime hkNow, TimeSpan? totalQuota = null)
    {
        var quota = totalQuota ?? TimeSpan.FromMinutes(90);

        var today = hkNow.Date;
        var noonToday = today.At(new LocalTime(12, 0));
        var noonTodayZoned = HongKongTimeZone.AtLeniently(noonToday);

        LocalDate cycleStartDate;
        if (hkNow.ToInstant() < noonTodayZoned.ToInstant())
        {
            // Before noon - we're in yesterday's cycle
            cycleStartDate = today.PlusDays(-1);
        }
        else
        {
            // At or after noon - we're in today's cycle
            cycleStartDate = today;
        }

        var cycleStart = cycleStartDate.At(new LocalTime(12, 0));
        var cycleStartZoned = HongKongTimeZone.AtLeniently(cycleStart);

        // Target date is the day after cycle start
        var targetDate = cycleStartDate.PlusDays(1);

        return new SleepCycle
        {
            CycleId = GetCycleId(cycleStartZoned),
            StartTime = cycleStartZoned,
            TargetDate = targetDate,
            State = SleepCycleState.Idle,
            QuotaRemaining = quota,
            TotalQuota = quota,
            DelayCount = 0,
            AutoDelayCount = 0
        };
    }

    /// <summary>
    /// Generates cycle ID from a cycle start time.
    /// Format: "2026-06-17T12:00" (ISO 8601)
    /// </summary>
    public static string GetCycleId(ZonedDateTime cycleStartTime)
    {
        var localDateTime = cycleStartTime.LocalDateTime;
        return localDateTime.ToString("yyyy-MM-ddTHH:mm", null);
    }

    /// <summary>
    /// Generates cycle ID for the cycle that contains the given moment.
    /// </summary>
    public static string GetCycleIdForMoment(Instant moment)
    {
        var cycle = GetCurrentCycle(moment);
        return cycle.CycleId;
    }

    /// <summary>
    /// Checks if two cycle IDs are the same.
    /// </summary>
    public static bool IsSameCycle(string cycleId1, string cycleId2)
    {
        return string.Equals(cycleId1, cycleId2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the target date from a cycle ID.
    /// Cycle ID "2026-06-17T12:00" -> target date is 2026-06-18
    /// </summary>
    public static LocalDate GetTargetDate(string cycleId)
    {
        var parts = cycleId.Split('T');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid cycle ID format: {cycleId}");
        }

        var dateParts = parts[0].Split('-');
        if (dateParts.Length != 3)
        {
            throw new ArgumentException($"Invalid cycle ID date format: {cycleId}");
        }

        var year = int.Parse(dateParts[0]);
        var month = int.Parse(dateParts[1]);
        var day = int.Parse(dateParts[2]);

        var startDate = new LocalDate(year, month, day);
        return startDate.PlusDays(1);
    }

    /// <summary>
    /// Gets the cycle start time from a cycle ID.
    /// Cycle ID "2026-06-17T12:00" -> ZonedDateTime at 2026-06-17 12:00 HK time
    /// </summary>
    public static ZonedDateTime GetCycleStartTime(string cycleId)
    {
        var pattern = LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-ddTHH:mm");
        var parseResult = pattern.Parse(cycleId);

        if (!parseResult.Success)
        {
            throw new ArgumentException($"Invalid cycle ID format: {cycleId}");
        }

        var localDateTime = parseResult.Value;
        return HongKongTimeZone.AtLeniently(localDateTime);
    }

    /// <summary>
    /// Checks if a given moment is within a specific cycle.
    /// </summary>
    public static bool IsMomentInCycle(Instant moment, string cycleId)
    {
        var currentCycleId = GetCycleIdForMoment(moment);
        return IsSameCycle(currentCycleId, cycleId);
    }
}

// ════════════════════════════════════════════════════════════
// 日程分類
// ════════════════════════════════════════════════════════════

/// <summary>
/// Pure functions for classifying days as early-class or non-early-class.
/// Early-class day = has qualifying course between 00:00 and the cutoff hour
/// (default 12:00, strict: before cutoff, not at cutoff).
/// Holidays are treated as NonEarlyClass (no early-class shutdown) but flagged
/// separately via <see cref="ScheduleTiming.IsHoliday"/> for UI display.
/// </summary>
public static class ScheduleClassifier
{
    /// <summary>
    /// Classifies a target date based on its morning class schedule.
    /// </summary>
    /// <param name="targetDate">The date to classify</param>
    /// <param name="events">All calendar events (pre-filtered for date range)</param>
    /// <param name="hasScheduleData">Whether we successfully fetched schedule data</param>
    /// <param name="cutoffHour">Hour (0-23) that separates early vs non-early class. Default 12 (noon).</param>
    /// <param name="holidays">Hong Kong public holidays (optional). Holiday dates are classified as NonEarlyClass.</param>
    /// <returns>Day classification</returns>
    public static DayClassification ClassifyDay(
        LocalDate targetDate,
        List<CalendarEvent> events,
        bool hasScheduleData = true,
        int cutoffHour = 12,
        List<PublicHoliday>? holidays = null)
    {
        if (!hasScheduleData)
        {
            return DayClassification.NoData;
        }

        // Holiday check: holidays are always treated as NonEarlyClass timing-wise
        if (IsHoliday(targetDate, holidays))
        {
            return DayClassification.NonEarlyClass;
        }

        var eventsOnDate = events
            .Where(e => e.Start.Date == targetDate)
            .Where(e => e.IsQualifyingCourse)
            .ToList();

        if (!eventsOnDate.Any())
        {
            return DayClassification.NonEarlyClass;
        }

        var cutoff = new LocalTime(cutoffHour, 0);
        var hasMorningClass = eventsOnDate.Any(e =>
        {
            var timeOfDay = e.Start.TimeOfDay;
            return timeOfDay >= LocalTime.Midnight && timeOfDay < cutoff;
        });

        return hasMorningClass ? DayClassification.EarlyClass : DayClassification.NonEarlyClass;
    }

    /// <summary>
    /// Checks if a target date is a Hong Kong public holiday.
    /// </summary>
    public static bool IsHoliday(LocalDate targetDate, List<PublicHoliday>? holidays)
    {
        return holidays?.Any(h => h.Date == targetDate) ?? false;
    }

    /// <summary>
    /// Gets the first (earliest) morning class on the target date.
    /// Returns null if no morning classes or if non-early-class day.
    /// </summary>
    /// <param name="cutoffHour">Hour (0-23) that separates early vs non-early class. Default 12 (noon).</param>
    public static CalendarEvent? GetFirstMorningClass(LocalDate targetDate, List<CalendarEvent> events, int cutoffHour = 12)
    {
        var cutoff = new LocalTime(cutoffHour, 0);
        return events
            .Where(e => e.Start.Date == targetDate)
            .Where(e => e.IsQualifyingCourse)
            .Where(e =>
            {
                var timeOfDay = e.Start.TimeOfDay;
                return timeOfDay >= LocalTime.Midnight && timeOfDay < cutoff;
            })
            .OrderBy(e => e.Start.TimeOfDay)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the earliest class time (as LocalTime) for a target date.
    /// </summary>
    public static LocalTime? GetFirstClassTime(LocalDate targetDate, List<CalendarEvent> events, int cutoffHour = 12)
    {
        var firstClass = GetFirstMorningClass(targetDate, events, cutoffHour);
        return firstClass?.Start.TimeOfDay;
    }
}

// ════════════════════════════════════════════════════════════
// 時間計算
// ════════════════════════════════════════════════════════════

/// <summary>
/// Pure functions for calculating warning and shutdown times based on day classification.
/// Implements the 9h15m buffer formula and minimum warning insurance.
/// </summary>
public static class TimingCalculator
{
    // Default buffer components (all configurable)
    public static readonly TimeSpan DefaultSleepTime = TimeSpan.FromHours(6);
    public static readonly TimeSpan DefaultPreSleepBuffer = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultWashTime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultBreakfastTime = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan DefaultCommuteTime = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(45);

    // Time bounds
    public static readonly LocalTime EarlyClassWarningLowerBound = new LocalTime(21, 30); // 21:30
    public static readonly LocalTime WarningUpperBound = new LocalTime(3, 0); // 03:00
    public static readonly LocalTime NonEarlyClassWarningTime = new LocalTime(0, 30); // 00:30
    public static readonly LocalTime NonEarlyClassHardShutdown = new LocalTime(3, 0); // 03:00

    // Constants
    public static readonly TimeSpan PreWarningAdvance = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinimumWarningGap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan EarlyClassQuota = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan DefaultDelayIncrement = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Calculates complete timing for a sleep cycle based on classification and first class time.
    /// </summary>
    /// <param name="isHoliday">If true, marks the resulting ScheduleTiming as a holiday (only meaningful for NonEarlyClass).</param>
    public static ScheduleTiming CalculateTiming(
        DayClassification classification,
        LocalTime? firstClassTime,
        TimeSpan? sleepTime = null,
        TimeSpan? preSleepBuffer = null,
        TimeSpan? washTime = null,
        TimeSpan? breakfastTime = null,
        TimeSpan? commuteTime = null,
        TimeSpan? earlyClassQuota = null,
        bool isHoliday = false)
    {
        var sleep = sleepTime ?? DefaultSleepTime;
        var preSleep = preSleepBuffer ?? DefaultPreSleepBuffer;
        var wash = washTime ?? DefaultWashTime;
        var breakfast = breakfastTime ?? DefaultBreakfastTime;
        var commute = commuteTime ?? DefaultCommuteTime;

        var totalBuffer = sleep + preSleep + wash + breakfast + commute;

        if (classification == DayClassification.EarlyClass && firstClassTime.HasValue)
        {
            var quota = earlyClassQuota ?? EarlyClassQuota;
            return CalculateEarlyClassTiming(firstClassTime.Value, totalBuffer, quota);
        }
        else
        {
            return CalculateNonEarlyClassTiming(isHoliday);
        }
    }

    /// <summary>
    /// Calculates timing for early-class days.
    /// Formula: warning = clamp(firstClass - buffer, 21:30, 03:00)
    /// Hard shutdown = min(warning + quota, 03:00)
    /// </summary>
    private static ScheduleTiming CalculateEarlyClassTiming(LocalTime firstClassTime, TimeSpan totalBuffer, TimeSpan quota)
    {
        var firstClassDateTime = LocalDate.FromDateTime(DateTime.Today).At(firstClassTime);
        var rawWarningDateTime = firstClassDateTime - Period.FromTicks(totalBuffer.Ticks);
        var rawWarningTime = rawWarningDateTime.TimeOfDay;

        if (rawWarningTime < LocalTime.Midnight)
        {
            rawWarningTime = rawWarningTime.PlusHours(24);
        }

        var warningTime = ClampTime(rawWarningTime, EarlyClassWarningLowerBound, WarningUpperBound);

        var hardShutdownTime = AddTimeWithWrap(warningTime, quota);
        if (hardShutdownTime > WarningUpperBound)
        {
            hardShutdownTime = WarningUpperBound;
        }

        var originalWarningTime = warningTime;
        var insuranceApplied = false;

        var gapMinutes = CalculateMinutesBetween(warningTime, hardShutdownTime);
        if (gapMinutes < MinimumWarningGap.TotalMinutes)
        {
            warningTime = SubtractTimeWithWrap(hardShutdownTime, MinimumWarningGap);
            insuranceApplied = true;
        }

        var preWarningTime = SubtractTimeWithWrap(warningTime, PreWarningAdvance);

        return new ScheduleTiming
        {
            Classification = DayClassification.EarlyClass,
            PreWarningTime = preWarningTime,
            WarningTime = warningTime,
            HardShutdownTime = hardShutdownTime,
            FirstClassTime = firstClassTime,
            TotalQuota = quota,
            IsUnlimitedManualDelay = false,
            MinimumWarningInsuranceApplied = insuranceApplied,
            OriginalWarningTime = insuranceApplied ? originalWarningTime : null
        };
    }

    /// <summary>
    /// Calculates timing for non-early-class days.
    /// Warning: 00:30, Hard shutdown: 03:00 (only for silent auto-delay)
    /// Manual delays are unlimited and can exceed 03:00.
    /// </summary>
    private static ScheduleTiming CalculateNonEarlyClassTiming(bool isHoliday = false)
    {
        var warningTime = NonEarlyClassWarningTime;
        var preWarningTime = SubtractTimeWithWrap(warningTime, PreWarningAdvance);

        return new ScheduleTiming
        {
            Classification = DayClassification.NonEarlyClass,
            PreWarningTime = preWarningTime,
            WarningTime = warningTime,
            HardShutdownTime = NonEarlyClassHardShutdown,
            FirstClassTime = null,
            TotalQuota = TimeSpan.Zero,
            IsUnlimitedManualDelay = true,
            MinimumWarningInsuranceApplied = false,
            OriginalWarningTime = null,
            IsHoliday = isHoliday
        };
    }

    /// <summary>
    /// Clamps a time between lower and upper bounds, handling day wrap-around.
    /// For range [21:30, 03:00], valid times are 21:30-23:59 and 00:00-03:00.
    /// </summary>
    private static LocalTime ClampTime(LocalTime time, LocalTime lower, LocalTime upper)
    {
        var timeMinutes = time.TickOfDay / NodaConstants.TicksPerMinute;
        var lowerMinutes = lower.TickOfDay / NodaConstants.TicksPerMinute;
        var upperMinutes = upper.TickOfDay / NodaConstants.TicksPerMinute;

        if (lowerMinutes > upperMinutes)
        {
            bool inValidRange = (timeMinutes >= lowerMinutes) || (timeMinutes <= upperMinutes);

            if (inValidRange)
            {
                return time;
            }
            else
            {
                var distToLower = timeMinutes > lowerMinutes
                    ? timeMinutes - lowerMinutes
                    : lowerMinutes - timeMinutes;
                var distToUpper = timeMinutes > upperMinutes
                    ? (24 * 60 - timeMinutes) + upperMinutes
                    : upperMinutes - timeMinutes;

                return distToLower <= distToUpper ? lower : upper;
            }
        }
        else
        {
            if (timeMinutes < lowerMinutes) return lower;
            if (timeMinutes > upperMinutes) return upper;
        }

        return time;
    }

    /// <summary>
    /// Adds a duration to a time, wrapping around midnight if necessary.
    /// </summary>
    private static LocalTime AddTimeWithWrap(LocalTime time, TimeSpan duration)
    {
        var totalTicks = time.TickOfDay + duration.Ticks;
        var ticksPerDay = NodaConstants.TicksPerDay;

        while (totalTicks >= ticksPerDay)
        {
            totalTicks -= ticksPerDay;
        }

        return LocalTime.FromTicksSinceMidnight(totalTicks);
    }

    /// <summary>
    /// Subtracts a duration from a time, wrapping around midnight if necessary.
    /// </summary>
    private static LocalTime SubtractTimeWithWrap(LocalTime time, TimeSpan duration)
    {
        var totalTicks = time.TickOfDay - duration.Ticks;
        var ticksPerDay = NodaConstants.TicksPerDay;

        while (totalTicks < 0)
        {
            totalTicks += ticksPerDay;
        }

        return LocalTime.FromTicksSinceMidnight(totalTicks);
    }

    /// <summary>
    /// Calculates minutes between two times, handling day wrap-around.
    /// </summary>
    private static double CalculateMinutesBetween(LocalTime start, LocalTime end)
    {
        var startMinutes = start.TickOfDay / NodaConstants.TicksPerMinute;
        var endMinutes = end.TickOfDay / NodaConstants.TicksPerMinute;

        if (endMinutes >= startMinutes)
        {
            return endMinutes - startMinutes;
        }
        else
        {
            return (24 * 60 - startMinutes) + endMinutes;
        }
    }
}
