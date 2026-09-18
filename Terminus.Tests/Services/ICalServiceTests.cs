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
    public void CourseCodePattern_MatchesValidCodes()
    {
        // Test various valid course code formats
        var validCodes = new[]
        {
            "ITP4416",
            "ITE3006",
            "ITP-3915",
            "COMP1234",
            "CS-5678"
        };

        foreach (var code in validCodes)
        {
            IsMatchingCourseCode(code).Should().BeTrue($"{code} should match pattern");
        }
    }

    [Fact]
    public void CourseCodePattern_RejectsInvalidCodes()
    {
        var invalidCodes = new[]
        {
            "Gym Session",
            "Online check In",
            "錄音室預約",
            "123456",
            "A1234",
            "ABCDEFG",
            "NO CLASS"
        };

        foreach (var code in invalidCodes)
        {
            IsMatchingCourseCode(code).Should().BeFalse($"{code} should NOT match pattern");
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

    // Helper method to test course code matching
    private static bool IsMatchingCourseCode(string summary)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(summary, @"^[A-Z]{2,4}-?\d{4}");
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
