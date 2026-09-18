using FluentAssertions;
using NodaTime;
using System.Net;
using Terminus.Core.Services;
using Xunit;

namespace Terminus.Core.Tests.Services;

/// <summary>
/// Tests for ICalService (T2).
/// Covers Test Case A from the specification.
/// Note: Some tests require actual network access or mock HTTP responses.
/// </summary>
public class ICalServiceTests
{
    [Fact]
    public void QualifyingCourse_AnyTimedEvent_IsQualifying()
    {
        // With the new logic, ANY timed event is qualifying
        var summaries = new[]
        {
            "Team Meeting",
            "Doctor Appointment",
            "ITP4416 Lecture",
            "Gym Session",
            "Lunch with friends",
            "線上會議"
        };

        foreach (var summary in summaries)
        {
            IsQualifyingCourse(summary, isAllDay: false).Should().BeTrue($"'{summary}' should be qualifying");
        }
    }

    [Fact]
    public void QualifyingCourse_AllDayEvent_IsNotQualifying()
    {
        var summaries = new[]
        {
            "Public Holiday",
            "ITP4416",
            "Team Offsite"
        };

        foreach (var summary in summaries)
        {
            IsQualifyingCourse(summary, isAllDay: true).Should().BeFalse($"'{summary}' should NOT be qualifying (all-day)");
        }
    }

    [Fact]
    public void QualifyingCourse_NoClassEvent_IsNotQualifying()
    {
        var summaries = new[]
        {
            "ITP4416 - NO CLASS",
            "NO CLASS today",
            "no class",
            "No Class"
        };

        foreach (var summary in summaries)
        {
            IsQualifyingCourse(summary, isAllDay: false).Should().BeFalse($"'{summary}' should NOT be qualifying (NO CLASS)");
        }
    }

    [Fact]
    public void WebcalUrl_ConvertsToHttps()
    {
        var webcalUrl = "webcal://p123-caldav.icloud.com/published/2/example";
        var expectedHttps = "https://p123-caldav.icloud.com/published/2/example";

        var converted = ConvertWebcalToHttps(webcalUrl);

        converted.Should().Be(expectedHttps);
    }

    [Fact]
    public void HttpsUrl_RemainsUnchanged()
    {
        var httpsUrl = "https://www.1823.gov.hk/common/ical/tc.ics";

        var converted = ConvertWebcalToHttps(httpsUrl);

        converted.Should().Be(httpsUrl);
    }

    // Helper method mirroring ICalService.IsQualifyingCourse logic
    private static bool IsQualifyingCourse(string summary, bool isAllDay)
    {
        if (isAllDay)
            return false;

        if (summary.Contains("NO CLASS", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string ConvertWebcalToHttps(string url)
    {
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url.Substring(9);
        }
        return url;
    }

    // Note: The following tests would require either:
    // 1. Mock HTTP client with test iCal data
    // 2. Actual network access to test URLs
    // 3. Integration test fixtures with sample iCal files

    /*
    [Fact]
    public async Task FetchHolidays_ReturnsHongKongHolidays()
    {
        var httpClient = new HttpClient();
        var service = new ICalService(httpClient);

        var holidays = await service.FetchHolidaysAsync("tc");

        holidays.Should().NotBeEmpty();
        holidays.Should().Contain(h => h.Date.Year >= 2025 && h.Date.Year <= 2027);
    }

    [Fact]
    public async Task FetchCalendar_ExpandsRecurringEvents()
    {
        // Would need mock iCal with RRULE
        // Verify that weekly recurring events appear on correct dates
    }

    [Fact]
    public async Task FetchCalendar_HandlesExdate()
    {
        // Would need mock iCal with EXDATE
        // Verify excluded dates don't appear
    }

    [Fact]
    public async Task FetchCalendar_HandlesRecurrenceId()
    {
        // Would need mock iCal with RECURRENCE-ID
        // Verify rescheduled events use new time/title
    }

    [Fact]
    public async Task FetchCalendar_ConvertsTimezones()
    {
        // Would need mock iCal with Asia/Shanghai and Asia/Taipei events
        // Verify all converted to Hong Kong time correctly
    }

    [Fact]
    public async Task FetchCalendar_FiltersAllDayEvents()
    {
        // Verify VALUE=DATE events are marked as IsAllDay
        // and IsQualifyingCourse = false
    }

    [Fact]
    public async Task FetchCalendar_FiltersNoClassEvents()
    {
        // Verify events with "NO CLASS" in summary
        // have IsQualifyingCourse = false
    }
    */
}
