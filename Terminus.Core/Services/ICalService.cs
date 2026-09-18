using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;
using System.Text.RegularExpressions;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// Service for fetching and parsing iCal calendar data.
/// Handles course schedules and public holidays with proper timezone conversion.
/// </summary>
public partial class ICalService
{
    private static readonly DateTimeZone HongKongTimeZone = DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"];
    private readonly HttpClient _httpClient;

    // Course code pattern: 2-4 uppercase letters, optional hyphen, 4 digits
    // Examples: ITP4416, ITE3006, ITP-3915
    [GeneratedRegex(@"^[A-Z]{2,4}-?\d{4}")]
    private static partial Regex CourseCodeRegex();

    public ICalService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Fetches and parses calendar from a webcal:// or https:// URL.
    /// Expands recurring events (RRULE), handles exclusions (EXDATE), and modifications (RECURRENCE-ID).
    /// Converts all events to Hong Kong timezone.
    /// </summary>
    /// <param name="url">Calendar URL (webcal:// will be converted to https://)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of calendar events with qualification status</returns>
    public async Task<List<Models.CalendarEvent>> FetchCalendarAsync(string url, CancellationToken cancellationToken = default)
    {
        // Convert webcal:// to https://
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url.Substring(9);
        }

        LoggerService.Info($"ICalService: 開始抓取日曆, URL={url.Substring(0, Math.Min(80, url.Length))}...");

        var icalContent = await _httpClient.GetStringAsync(url, cancellationToken);

        LoggerService.Info($"ICalService: 收到回應, 長度={icalContent.Length} 字元");

        if (icalContent.Length == 0)
        {
            LoggerService.Warn("ICalService: 回應內容為空");
            return new List<Models.CalendarEvent>();
        }

        var calendar = Calendar.Load(icalContent);

        LoggerService.Info($"ICalService: 解析完成, 原始事件數={calendar.Events.Count}");

        var events = new List<Models.CalendarEvent>();

        // Use a reasonable date range instead of MinValue/MaxValue to avoid NodaTime overflow
        var rangeStart = new CalDateTime(DateTime.Today.AddYears(-2));
        var rangeEnd = new CalDateTime(DateTime.Today.AddYears(2));

        foreach (var calendarEvent in calendar.Events)
        {
            var summary = calendarEvent.Summary ?? string.Empty;

            try
            {
                // Get occurrences (handles RRULE expansion) - use bounded range to prevent overflow
                var occurrences = calendarEvent.GetOccurrences(rangeStart, rangeEnd);

                LoggerService.Info($"ICalService: 事件 '{summary}', 出現次數={occurrences.Count}");

                foreach (var occurrence in occurrences)
                {
                    var startTime = ConvertToHongKongTime(occurrence.Period.StartTime);
                    var endTime = ConvertToHongKongTime(occurrence.Period.EndTime);

                    var isAllDay = !occurrence.Period.StartTime.HasTime;

                    // Check if this event qualifies as a course
                    var isQualifying = IsQualifyingCourse(summary, isAllDay);

                    events.Add(new Models.CalendarEvent
                    {
                        Summary = summary,
                        Start = startTime,
                        End = endTime,
                        IsAllDay = isAllDay,
                        Uid = calendarEvent.Uid,
                        Location = calendarEvent.Location,
                        IsQualifyingCourse = isQualifying
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"ICalService: 事件 '{summary}' 展開失敗, 跳過: {ex.Message}");
            }
        }

        LoggerService.Info($"ICalService: 展開後總事件數={events.Count}");
        return events;
    }

    /// <summary>
    /// Fetches Hong Kong public holidays from 1823.gov.hk.
    /// </summary>
    /// <param name="language">Language code: tc (traditional Chinese), sc (simplified), or en (English)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of public holidays</returns>
    public async Task<List<PublicHoliday>> FetchHolidaysAsync(string language = "tc", CancellationToken cancellationToken = default)
    {
        var url = $"https://www.1823.gov.hk/common/ical/{language}.ics";
        var icalContent = await _httpClient.GetStringAsync(url, cancellationToken);
        var calendar = Calendar.Load(icalContent);

        var holidays = new List<PublicHoliday>();

        foreach (var calendarEvent in calendar.Events)
        {
            // Public holidays are typically all-day events
            var occurrences = calendarEvent.GetOccurrences(
                new CalDateTime(DateTime.Today.AddYears(-2)),
                new CalDateTime(DateTime.Today.AddYears(2))
            );

            foreach (var occurrence in occurrences)
            {
                var startTime = ConvertToHongKongTime(occurrence.Period.StartTime);

                holidays.Add(new PublicHoliday
                {
                    Name = calendarEvent.Summary ?? "Unknown Holiday",
                    Date = startTime.Date,
                    Uid = calendarEvent.Uid
                });
            }
        }

        return holidays;
    }

    /// <summary>
    /// Converts CalDateTime to Hong Kong ZonedDateTime.
    /// Handles mixed timezones (Asia/Shanghai, Asia/Taipei, etc.) by converting to HK time.
    /// </summary>
    private static ZonedDateTime ConvertToHongKongTime(IDateTime calDateTime)
    {
        try
        {
            // Log the raw values for debugging
            LoggerService.Info($"ICalService: ConvertToHongKongTime: Value={calDateTime.Value:yyyy-MM-dd HH:mm:ss}, TzId={calDateTime.TzId ?? "null"}, AsUtc={calDateTime.AsUtc:yyyy-MM-dd HH:mm:ss}, HasTime={calDateTime.HasTime}");

            DateTime utcDt;

            // Try AsUtc first (most reliable when available)
            if (calDateTime.AsUtc != DateTime.MinValue && calDateTime.AsUtc != default)
            {
                utcDt = calDateTime.AsUtc;
            }
            else if (!string.IsNullOrEmpty(calDateTime.TzId))
            {
                // Has timezone ID - use NodaTime to convert properly
                try
                {
                    var tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(calDateTime.TzId)
                             ?? DateTimeZoneProviders.Tzdb.GetZoneOrNull("Asia/Hong_Kong")!;
                    var localDt = LocalDateTime.FromDateTime(calDateTime.Value);
                    var zoned = tz.AtLeniently(localDt);
                    utcDt = zoned.ToDateTimeUtc();
                }
                catch
                {
                    // Fallback: assume the Value is already UTC
                    utcDt = DateTime.SpecifyKind(calDateTime.Value, DateTimeKind.Utc);
                }
            }
            else
            {
                // No timezone info - assume it's already UTC
                utcDt = DateTime.SpecifyKind(calDateTime.Value, DateTimeKind.Utc);
            }

            var instant = Instant.FromDateTimeUtc(utcDt);
            var result = instant.InZone(HongKongTimeZone);
            LoggerService.Info($"ICalService: ConvertToHongKongTime: 結果={result.ToDateTimeUnspecified():yyyy-MM-dd HH:mm:ss}");
            return result;
        }
        catch (Exception ex)
        {
            LoggerService.Error($"ICalService: ConvertToHongKongTime 失敗: {ex.Message}", ex);
            // Ultimate fallback: use Value as UTC
            var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(calDateTime.Value, DateTimeKind.Utc));
            return instant.InZone(HongKongTimeZone);
        }
    }

    /// <summary>
    /// Determines if an event qualifies as a course based on spec criteria:
    /// - SUMMARY matches ^[A-Z]{2,4}-?\d{4}
    /// - NOT an all-day event (VALUE=DATE)
    /// - SUMMARY does NOT contain "NO CLASS"
    /// </summary>
    private static bool IsQualifyingCourse(string summary, bool isAllDay)
    {
        if (isAllDay)
            return false;

        if (summary.Contains("NO CLASS", StringComparison.OrdinalIgnoreCase))
            return false;

        return CourseCodeRegex().IsMatch(summary);
    }
}
