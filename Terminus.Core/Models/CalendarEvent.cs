using NodaTime;

namespace Terminus.Core.Models;

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
