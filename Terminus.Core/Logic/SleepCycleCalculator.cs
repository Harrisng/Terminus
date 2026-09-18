using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Logic;

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

        // Determine which cycle we're in
        // If before 12:00 today, we're in yesterday's cycle
        // If at or after 12:00 today, we're in today's cycle

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
        // Parse the cycle ID to get the start date, then add 1 day
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
        // Parse "2026-06-17T12:00"
        var pattern = NodaTime.Text.LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-ddTHH:mm");
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
