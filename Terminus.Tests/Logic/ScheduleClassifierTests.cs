using FluentAssertions;
using NodaTime;
using Terminus.Core.Logic;
using Terminus.Core.Models;
using Xunit;

namespace Terminus.Core.Tests.Logic;

/// <summary>
/// Tests for ScheduleClassifier (T4).
/// Covers Test Case B from the specification.
/// </summary>
public class ScheduleClassifierTests
{
    private static readonly DateTimeZone HongKongTimeZone = DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"];

    [Fact]
    public void TestCaseB_0930Class_IsEarlyClassDay()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 9, 30, "ITP4416", isQualifying: true)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.EarlyClass);
    }

    [Fact]
    public void TestCaseB_1330ClassOnly_IsNonEarlyClassDay()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 13, 30, "ITE3006", isQualifying: true)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void TestCaseB_ClassExactlyAt1200_IsNonEarlyClassDay()
    {
        // Edge case: class exactly at 12:00 is NON-early-class
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 12, 0, "ITP3915", isQualifying: true)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void TestCaseB_ClassAt1130_IsEarlyClassDay()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 11, 30, "ITP4416", isQualifying: true)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.EarlyClass);
    }

    [Fact]
    public void TestCaseB_WeekendNoClass_IsNonEarlyClassDay()
    {
        var targetDate = new LocalDate(2026, 6, 20); // Saturday
        var events = new List<CalendarEvent>(); // No classes

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void TestCaseB_NoScheduleData_IsNoData()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>();

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events, hasScheduleData: false);

        classification.Should().Be(DayClassification.NoData);
    }

    [Fact]
    public void GetFirstMorningClass_MultipleClasses_ReturnsEarliest()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 11, 30, "ITP4416", isQualifying: true),
            CreateEvent(targetDate, 9, 30, "ITE3006", isQualifying: true),
            CreateEvent(targetDate, 14, 0, "ITP3915", isQualifying: true)
        };

        var firstClass = ScheduleClassifier.GetFirstMorningClass(targetDate, events);

        firstClass.Should().NotBeNull();
        firstClass!.Start.TimeOfDay.Should().Be(new LocalTime(9, 30));
        firstClass.Summary.Should().Be("ITE3006");
    }

    [Fact]
    public void GetFirstMorningClass_OnlyAfternoonClasses_ReturnsNull()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 13, 30, "ITE3006", isQualifying: true),
            CreateEvent(targetDate, 15, 0, "ITP3915", isQualifying: true)
        };

        var firstClass = ScheduleClassifier.GetFirstMorningClass(targetDate, events);

        firstClass.Should().BeNull();
    }

    [Fact]
    public void ClassifyDay_NonQualifyingEvents_TreatedAsNonEarlyClass()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            // Non-qualifying events (bookings, gym, etc.)
            CreateEvent(targetDate, 9, 30, "Gym Session", isQualifying: false),
            CreateEvent(targetDate, 10, 0, "Online check In", isQualifying: false)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void ClassifyDay_MixedQualifyingAndNonQualifying_UsesOnlyQualifying()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 8, 0, "Gym", isQualifying: false),
            CreateEvent(targetDate, 13, 30, "ITP4416", isQualifying: true) // Only afternoon class
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        // Should be non-early because qualifying class is in afternoon
        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void ClassifyDay_AllDayEvent_IsExcluded()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEventAllDay(targetDate, "ITP4416 - NO CLASS", isQualifying: false)
        };

        var classification = ScheduleClassifier.ClassifyDay(targetDate, events);

        classification.Should().Be(DayClassification.NonEarlyClass);
    }

    [Fact]
    public void GetFirstClassTime_ReturnsCorrectTime()
    {
        var targetDate = new LocalDate(2026, 6, 18);
        var events = new List<CalendarEvent>
        {
            CreateEvent(targetDate, 9, 30, "ITP4416", isQualifying: true)
        };

        var firstClassTime = ScheduleClassifier.GetFirstClassTime(targetDate, events);

        firstClassTime.Should().NotBeNull();
        firstClassTime.Should().Be(new LocalTime(9, 30));
    }

    private static CalendarEvent CreateEvent(LocalDate date, int hour, int minute, string summary, bool isQualifying)
    {
        var localDateTime = date.At(new LocalTime(hour, minute));
        var zonedStart = HongKongTimeZone.AtLeniently(localDateTime);
        var zonedEnd = zonedStart.Plus(Duration.FromHours(2)); // 2-hour class

        return new CalendarEvent
        {
            Summary = summary,
            Start = zonedStart,
            End = zonedEnd,
            IsAllDay = false,
            IsQualifyingCourse = isQualifying
        };
    }

    private static CalendarEvent CreateEventAllDay(LocalDate date, string summary, bool isQualifying)
    {
        var localDateTime = date.At(LocalTime.Midnight);
        var zonedStart = HongKongTimeZone.AtLeniently(localDateTime);

        return new CalendarEvent
        {
            Summary = summary,
            Start = zonedStart,
            End = zonedStart.Plus(Duration.FromDays(1)),
            IsAllDay = true,
            IsQualifyingCourse = isQualifying
        };
    }
}
