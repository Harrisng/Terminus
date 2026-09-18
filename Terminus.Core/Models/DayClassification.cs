namespace Terminus.Core.Models;

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
