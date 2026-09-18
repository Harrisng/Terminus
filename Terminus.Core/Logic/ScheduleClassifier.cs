using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Logic;

/// <summary>
/// Pure functions for classifying days as early-class or non-early-class.
/// Early-class day = has qualifying course between 00:00 and the cutoff hour
/// (default 12:00, strict: before cutoff, not at cutoff).
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
    /// <returns>Day classification</returns>
    public static DayClassification ClassifyDay(
        LocalDate targetDate,
        List<CalendarEvent> events,
        bool hasScheduleData = true,
        int cutoffHour = 12)
    {
        if (!hasScheduleData)
        {
            return DayClassification.NoData;
        }

        // Filter events for the target date
        var eventsOnDate = events
            .Where(e => e.Start.Date == targetDate)
            .Where(e => e.IsQualifyingCourse)
            .ToList();

        if (!eventsOnDate.Any())
        {
            return DayClassification.NonEarlyClass;
        }

        // Check if any qualifying event starts between 00:00 and cutoff hour (exclusive)
        // Edge case: class exactly at cutoff hour is NON-early-class
        var cutoff = new LocalTime(cutoffHour, 0);
        var hasMorningClass = eventsOnDate.Any(e =>
        {
            var timeOfDay = e.Start.TimeOfDay;
            return timeOfDay >= LocalTime.Midnight && timeOfDay < cutoff;
        });

        return hasMorningClass ? DayClassification.EarlyClass : DayClassification.NonEarlyClass;
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
    /// <param name="cutoffHour">Hour (0-23) that separates early vs non-early class. Default 12 (noon).</param>
    public static LocalTime? GetFirstClassTime(LocalDate targetDate, List<CalendarEvent> events, int cutoffHour = 12)
    {
        var firstClass = GetFirstMorningClass(targetDate, events, cutoffHour);
        return firstClass?.Start.TimeOfDay;
    }
}
