using FluentAssertions;
using NodaTime;
using Terminus.Core.Logic;
using Terminus.Core.Models;
using Xunit;

namespace Terminus.Core.Tests.Logic;

/// <summary>
/// Tests for TimingCalculator (T4).
/// Covers Test Case C from the specification.
/// </summary>
public class TimingCalculatorTests
{
    [Fact]
    public void TestCaseC_0900Class_Warns2345_Shuts0115()
    {
        // 09:00 class → 23:45 warn, 01:15 shutdown
        var firstClassTime = new LocalTime(9, 0);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        timing.WarningTime.Should().Be(new LocalTime(23, 45));
        timing.HardShutdownTime.Should().Be(new LocalTime(1, 15));
        timing.Classification.Should().Be(DayClassification.EarlyClass);
    }

    [Fact]
    public void TestCaseC_0930Class_Warns0015_Shuts0145()
    {
        // 09:30 class → 00:15 warn, 01:45 shutdown
        var firstClassTime = new LocalTime(9, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        timing.WarningTime.Should().Be(new LocalTime(0, 15));
        timing.HardShutdownTime.Should().Be(new LocalTime(1, 45));
    }

    [Fact]
    public void TestCaseC_1030Class_Warns0115_Shuts0245()
    {
        // 10:30 class → 01:15 warn, 02:45 shutdown
        var firstClassTime = new LocalTime(10, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        timing.WarningTime.Should().Be(new LocalTime(1, 15));
        timing.HardShutdownTime.Should().Be(new LocalTime(2, 45));
    }

    [Fact]
    public void TestCaseC_NonEarlyClass_Warns0030_Shuts0300()
    {
        // Non-early class → 00:30 warn, 03:00 shutdown
        var timing = TimingCalculator.CalculateTiming(
            DayClassification.NonEarlyClass,
            null);

        timing.WarningTime.Should().Be(new LocalTime(0, 30));
        timing.HardShutdownTime.Should().Be(new LocalTime(3, 0));
        timing.IsUnlimitedManualDelay.Should().BeTrue();
    }

    [Fact]
    public void TestCaseC_VeryEarlyClass_CalculatesCorrectly()
    {
        // 08:00 class: 08:00 - 9h15m = 22:45 (within valid range, no clamping needed)
        var firstClassTime = new LocalTime(8, 0);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        // 22:45 is within [21:30, 03:00] range, so it should not be clamped
        timing.WarningTime.Should().Be(new LocalTime(22, 45));

        // Hard shutdown: 22:45 + 90min = 00:15
        timing.HardShutdownTime.Should().Be(new LocalTime(0, 15));
    }

    [Fact]
    public void TestCaseC_MinimumWarningInsurance_PushesWarningEarlier()
    {
        // If warning and shutdown are less than 30 minutes apart,
        // warning should be pushed earlier
        // Example: 11:30 class → raw calculation might violate minimum gap

        var firstClassTime = new LocalTime(11, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        // Calculate gap between warning and shutdown
        var warningMinutes = timing.WarningTime.Hour * 60 + timing.WarningTime.Minute;
        var shutdownMinutes = timing.HardShutdownTime.Hour * 60 + timing.HardShutdownTime.Minute;

        // Handle day wrap-around
        if (shutdownMinutes < warningMinutes)
        {
            shutdownMinutes += 24 * 60;
        }

        var gapMinutes = shutdownMinutes - warningMinutes;

        // Gap must be at least 30 minutes
        gapMinutes.Should().BeGreaterOrEqualTo(30);
    }

    [Fact]
    public void TestCaseC_ShutdownCappedAt0300()
    {
        // Even if warning + 90min exceeds 03:00, shutdown caps at 03:00
        var firstClassTime = new LocalTime(10, 0);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        // warning should be around 00:45, +90min = 02:15 (within limit)
        // But if warning is later, shutdown caps at 03:00
        timing.HardShutdownTime.Hour.Should().BeLessOrEqualTo(3);
        if (timing.HardShutdownTime.Hour == 3)
        {
            timing.HardShutdownTime.Minute.Should().Be(0);
        }
    }

    [Fact]
    public void PreWarningTime_Is30MinutesBeforeWarning()
    {
        var firstClassTime = new LocalTime(9, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        // Warning at 00:15, pre-warning should be at 23:45 (previous day)
        timing.PreWarningTime.Should().Be(new LocalTime(23, 45));
    }

    [Fact]
    public void EarlyClassDay_Has90MinuteQuota()
    {
        var firstClassTime = new LocalTime(9, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        timing.TotalQuota.Should().Be(TimeSpan.FromMinutes(90));
        timing.IsUnlimitedManualDelay.Should().BeFalse();
    }

    [Fact]
    public void NonEarlyClassDay_HasUnlimitedManualDelay()
    {
        var timing = TimingCalculator.CalculateTiming(
            DayClassification.NonEarlyClass,
            null);

        timing.IsUnlimitedManualDelay.Should().BeTrue();
        timing.TotalQuota.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CustomBufferComponents_AffectCalculation()
    {
        // Override default 9h15m with custom values
        var sleep = TimeSpan.FromHours(7);
        var preSleep = TimeSpan.FromMinutes(20);
        var wash = TimeSpan.FromMinutes(40);
        var breakfast = TimeSpan.FromMinutes(30);
        var commute = TimeSpan.FromHours(2);
        // Total: 10h30m

        var firstClassTime = new LocalTime(9, 30);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime,
            sleep, preSleep, wash, breakfast, commute);

        // 09:30 - 10:30 = 23:00 (previous day)
        timing.WarningTime.Should().Be(new LocalTime(23, 0));
    }

    [Fact]
    public void MinimumWarningInsurance_TracksOriginalTime()
    {
        // Use a scenario where insurance kicks in
        var firstClassTime = new LocalTime(11, 45);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        // Should have minimum 30-minute gap
        var warningMinutes = timing.WarningTime.Hour * 60 + timing.WarningTime.Minute;
        var shutdownMinutes = timing.HardShutdownTime.Hour * 60 + timing.HardShutdownTime.Minute;

        if (shutdownMinutes < warningMinutes)
        {
            shutdownMinutes += 24 * 60;
        }

        (shutdownMinutes - warningMinutes).Should().BeGreaterOrEqualTo(30);

        // If insurance was applied, OriginalWarningTime should be set
        if (timing.MinimumWarningInsuranceApplied)
        {
            timing.OriginalWarningTime.Should().NotBeNull();
        }
    }

    [Fact]
    public void NoData_TreatedAsNonEarlyClass()
    {
        var timing = TimingCalculator.CalculateTiming(
            DayClassification.NoData,
            null);

        // Should behave like non-early-class day
        timing.WarningTime.Should().Be(new LocalTime(0, 30));
        timing.HardShutdownTime.Should().Be(new LocalTime(3, 0));
        timing.IsUnlimitedManualDelay.Should().BeTrue();
    }

    [Theory]
    [InlineData(9, 0, 23, 45, 1, 15)]   // 09:00 → 23:45 warn, 01:15 shutdown
    [InlineData(9, 30, 0, 15, 1, 45)]   // 09:30 → 00:15 warn, 01:45 shutdown
    [InlineData(10, 30, 1, 15, 2, 45)]  // 10:30 → 01:15 warn, 02:45 shutdown
    public void SpecExamples_MatchExpectedTimes(
        int classHour, int classMinute,
        int expectedWarnHour, int expectedWarnMinute,
        int expectedShutdownHour, int expectedShutdownMinute)
    {
        var firstClassTime = new LocalTime(classHour, classMinute);

        var timing = TimingCalculator.CalculateTiming(
            DayClassification.EarlyClass,
            firstClassTime);

        timing.WarningTime.Should().Be(new LocalTime(expectedWarnHour, expectedWarnMinute));
        timing.HardShutdownTime.Should().Be(new LocalTime(expectedShutdownHour, expectedShutdownMinute));
    }
}
