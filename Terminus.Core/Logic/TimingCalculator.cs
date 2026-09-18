using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Logic;

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
    public static ScheduleTiming CalculateTiming(
        DayClassification classification,
        LocalTime? firstClassTime,
        TimeSpan? sleepTime = null,
        TimeSpan? preSleepBuffer = null,
        TimeSpan? washTime = null,
        TimeSpan? breakfastTime = null,
        TimeSpan? commuteTime = null,
        TimeSpan? earlyClassQuota = null)
    {
        var sleep = sleepTime ?? DefaultSleepTime;
        var preSleep = preSleepBuffer ?? DefaultPreSleepBuffer;
        var wash = washTime ?? DefaultWashTime;
        var breakfast = breakfastTime ?? DefaultBreakfastTime;
        var commute = commuteTime ?? DefaultCommuteTime;

        // Total buffer: 9h15m = 6h + 0:15 + 0:30 + 0:45 + 1:45
        var totalBuffer = sleep + preSleep + wash + breakfast + commute;

        if (classification == DayClassification.EarlyClass && firstClassTime.HasValue)
        {
            var quota = earlyClassQuota ?? EarlyClassQuota;
            return CalculateEarlyClassTiming(firstClassTime.Value, totalBuffer, quota);
        }
        else
        {
            // Non-early-class or NoData - treat the same way
            return CalculateNonEarlyClassTiming();
        }
    }

    /// <summary>
    /// Calculates timing for early-class days.
    /// Formula: warning = clamp(firstClass - buffer, 21:30, 03:00)
    /// Hard shutdown = min(warning + quota, 03:00)
    /// </summary>
    private static ScheduleTiming CalculateEarlyClassTiming(LocalTime firstClassTime, TimeSpan totalBuffer, TimeSpan quota)
    {
        // Calculate raw warning time
        var firstClassDateTime = LocalDate.FromDateTime(DateTime.Today).At(firstClassTime);
        var rawWarningDateTime = firstClassDateTime - Period.FromTicks(totalBuffer.Ticks);
        var rawWarningTime = rawWarningDateTime.TimeOfDay;

        // Handle day boundary crossing (if warning is negative, add 24 hours)
        if (rawWarningTime < LocalTime.Midnight)
        {
            rawWarningTime = rawWarningTime.PlusHours(24);
        }

        // Clamp to bounds [21:30, 03:00]
        var warningTime = ClampTime(rawWarningTime, EarlyClassWarningLowerBound, WarningUpperBound);

        // Calculate hard shutdown: min(warning + quota, 03:00)
        var hardShutdownTime = AddTimeWithWrap(warningTime, quota);
        if (hardShutdownTime > WarningUpperBound)
        {
            hardShutdownTime = WarningUpperBound;
        }

        // Apply minimum warning insurance (30 min gap between warning and shutdown)
        var originalWarningTime = warningTime;
        var insuranceApplied = false;

        var gapMinutes = CalculateMinutesBetween(warningTime, hardShutdownTime);
        if (gapMinutes < MinimumWarningGap.TotalMinutes)
        {
            // Push warning earlier
            warningTime = SubtractTimeWithWrap(hardShutdownTime, MinimumWarningGap);
            insuranceApplied = true;
        }

        // Pre-warning is 30 minutes before warning
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
    private static ScheduleTiming CalculateNonEarlyClassTiming()
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
            TotalQuota = TimeSpan.Zero, // No quota limit for manual delays
            IsUnlimitedManualDelay = true,
            MinimumWarningInsuranceApplied = false,
            OriginalWarningTime = null
        };
    }

    /// <summary>
    /// Clamps a time between lower and upper bounds, handling day wrap-around.
    /// For range [21:30, 03:00], valid times are 21:30-23:59 and 00:00-03:00.
    /// </summary>
    private static LocalTime ClampTime(LocalTime time, LocalTime lower, LocalTime upper)
    {
        // Convert to minutes since midnight for easier comparison
        var timeMinutes = time.TickOfDay / NodaConstants.TicksPerMinute;
        var lowerMinutes = lower.TickOfDay / NodaConstants.TicksPerMinute;
        var upperMinutes = upper.TickOfDay / NodaConstants.TicksPerMinute;

        // Handle wrap-around case (e.g., 21:30 to 03:00 next day)
        if (lowerMinutes > upperMinutes)
        {
            // Range crosses midnight
            // Valid range: [21:30, 23:59] OR [00:00, 03:00]
            // Invalid/gap range: [03:01, 21:29]

            bool inValidRange = (timeMinutes >= lowerMinutes) || (timeMinutes <= upperMinutes);

            if (inValidRange)
            {
                return time; // Already in range
            }
            else
            {
                // Outside range - in the gap (e.g., 04:00 to 21:29)
                // Pick closest bound
                var distToLower = timeMinutes > lowerMinutes
                    ? timeMinutes - lowerMinutes  // After lower, wrap back
                    : lowerMinutes - timeMinutes;  // Before lower, go back
                var distToUpper = timeMinutes > upperMinutes
                    ? (24 * 60 - timeMinutes) + upperMinutes  // Forward through midnight
                    : upperMinutes - timeMinutes;  // Before upper, go forward

                return distToLower <= distToUpper ? lower : upper;
            }
        }
        else
        {
            // Normal range (no wrap-around)
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
    /// Assumes the end time is "after" the start time (may be next day).
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
            // Crosses midnight
            return (24 * 60 - startMinutes) + endMinutes;
        }
    }
}
