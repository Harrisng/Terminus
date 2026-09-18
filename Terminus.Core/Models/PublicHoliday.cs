using NodaTime;

namespace Terminus.Core.Models;

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
