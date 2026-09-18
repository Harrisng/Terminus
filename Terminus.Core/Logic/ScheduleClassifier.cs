using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Logic;

/// <summary>
/// Pure functions for classifying days as early-class or non-early-class.
/// Early-class day = has qualifying course between 00:00-12:00 (strict: before 12:00, not at 12:00).
/// </summary>
public static class ScheduleClassifier
{
    /// <summary>
    /// Classifies a target date based on its morning class schedule.
    /// </summary>
    /// <param name="targetDate">The date to classify</param>
    /// <param name="events">All calendar events (pre-filtered for date range)</param>
    /// <param name="hasScheduleData">Whether we successfully fetched schedule data</param>
    /// <returns>Day classification</returns>
    public static DayClassification ClassifyDay(
        LocalDate targetDate,
        List<CalendarEvent> events,
        bool hasScheduleData = true)
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

        // Check if any qualifying event starts between 00:00 and 12:00 (exclusive)
        // Edge case: class exactly at 12:00 is NON-early-class
        var hasMorningClass = eventsOnDate.Any(e =>
        {
            var timeOfDay = e.Start.TimeOfDay;
            return timeOfDay >= LocalTime.Midnight && timeOfDay < new LocalTime(12, 0);
        });

        return hasMorningClass ? DayClassification.EarlyClass : DayClassification.NonEarlyClass;
    }

    /// <summary>
    /// Gets the first (earliest) morning class on the target date.
    /// Returns null if no morning classes or if non-early-class day.
    /// </summary>
    public static CalendarEvent? GetFirstMorningClass(LocalDate targetDate, List<CalendarEvent> events)
    {
        return events
            .Where(e => e.Start.Date == targetDate)
            .Where(e => e.IsQualifyingCourse)
            .Where(e =>
            {
                var timeOfDay = e.Start.TimeOfDay;
                return timeOfDay >= LocalTime.Midnight && timeOfDay < new LocalTime(12, 0);
            })
            .OrderBy(e => e.Start.TimeOfDay)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the earliest class time (as LocalTime) for a target date.
    /// </summary>
    public static LocalTime? GetFirstClassTime(LocalDate targetDate, List<CalendarEvent> events)
    {
        var firstClass = GetFirstMorningClass(targetDate, events);
        return firstClass?.Start.TimeOfDay;
    }
}
