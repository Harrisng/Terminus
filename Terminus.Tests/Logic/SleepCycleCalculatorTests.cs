using FluentAssertions;
using NodaTime;
using Terminus.Core.Logic;
using Xunit;

namespace Terminus.Core.Tests.Logic;

/// <summary>
/// Tests for SleepCycleCalculator (T20).
/// Covers Test Case G from the specification.
/// </summary>
public class SleepCycleCalculatorTests
{
    private static readonly DateTimeZone HongKongTimeZone = DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"];

    [Fact]
    public void GetCurrentCycle_BeforeNoon_BelongsToYesterdaysCycle()
    {
        // June 17, 2026 at 11:59 should belong to June 16 12:00 cycle
        var moment = CreateHkInstant(2026, 6, 17, 11, 59);

        var cycle = SleepCycleCalculator.GetCurrentCycle(moment);

        cycle.CycleId.Should().Be("2026-06-16T12:00");
        cycle.TargetDate.Should().Be(new LocalDate(2026, 6, 17));
    }

    [Fact]
    public void GetCurrentCycle_AtNoon_BelongsToTodaysCycle()
    {
        // June 17, 2026 at 12:00 should belong to June 17 12:00 cycle
        var moment = CreateHkInstant(2026, 6, 17, 12, 0);

        var cycle = SleepCycleCalculator.GetCurrentCycle(moment);

        cycle.CycleId.Should().Be("2026-06-17T12:00");
        cycle.TargetDate.Should().Be(new LocalDate(2026, 6, 18));
    }

    [Fact]
    public void GetCurrentCycle_AfterNoon_BelongsToTodaysCycle()
    {
        // June 17, 2026 at 12:01 should belong to June 17 12:00 cycle
        var moment = CreateHkInstant(2026, 6, 17, 12, 1);

        var cycle = SleepCycleCalculator.GetCurrentCycle(moment);

        cycle.CycleId.Should().Be("2026-06-17T12:00");
        cycle.TargetDate.Should().Be(new LocalDate(2026, 6, 18));
    }

    [Fact]
    public void TestCaseG_TuesdayWarningAndWednesdayPromptSameCycle()
    {
        // Tuesday 23:45 warning
        var tuesdayWarning = CreateHkInstant(2026, 6, 16, 23, 45);
        var tuesdayCycle = SleepCycleCalculator.GetCurrentCycle(tuesdayWarning);

        // Wednesday 00:15 prompt (after midnight)
        var wednesdayPrompt = CreateHkInstant(2026, 6, 17, 0, 15);
        var wednesdayCycle = SleepCycleCalculator.GetCurrentCycle(wednesdayPrompt);

        // Both should be in the same cycle (June 16 12:00)
        tuesdayCycle.CycleId.Should().Be("2026-06-16T12:00");
        wednesdayCycle.CycleId.Should().Be("2026-06-16T12:00");
        SleepCycleCalculator.IsSameCycle(tuesdayCycle.CycleId, wednesdayCycle.CycleId).Should().BeTrue();
    }

    [Fact]
    public void TestCaseG_DelayAt1150And1155StillSameCycle()
    {
        // Non-early day: delay pressed at 11:50
        var delay1150 = CreateHkInstant(2026, 6, 17, 11, 50);
        var cycle1150 = SleepCycleCalculator.GetCurrentCycle(delay1150);

        // Delay effect at 11:55 (still before noon)
        var delay1155 = CreateHkInstant(2026, 6, 17, 11, 55);
        var cycle1155 = SleepCycleCalculator.GetCurrentCycle(delay1155);

        // Both should be in June 16's cycle (before noon = yesterday's cycle)
        cycle1150.CycleId.Should().Be("2026-06-16T12:00");
        cycle1155.CycleId.Should().Be("2026-06-16T12:00");
        SleepCycleCalculator.IsSameCycle(cycle1150.CycleId, cycle1155.CycleId).Should().BeTrue();
    }

    [Fact]
    public void TestCaseG_AfterShutdownAt1200NewCycle()
    {
        // Shutdown at 03:00 (June 17)
        var shutdown = CreateHkInstant(2026, 6, 17, 3, 0);
        var shutdownCycle = SleepCycleCalculator.GetCurrentCycle(shutdown);
        shutdownCycle.CycleId.Should().Be("2026-06-16T12:00");

        // At 12:00 (June 17) - new cycle starts
        var newCycleStart = CreateHkInstant(2026, 6, 17, 12, 0);
        var newCycle = SleepCycleCalculator.GetCurrentCycle(newCycleStart);
        newCycle.CycleId.Should().Be("2026-06-17T12:00");

        SleepCycleCalculator.IsSameCycle(shutdownCycle.CycleId, newCycle.CycleId).Should().BeFalse();
    }

    [Fact]
    public void TestCaseG_RebootAt0200BelongsToYesterdaysCycle()
    {
        // Reboot at 02:00 (June 17)
        var reboot = CreateHkInstant(2026, 6, 17, 2, 0);
        var cycle = SleepCycleCalculator.GetCurrentCycle(reboot);

        // Should belong to June 16 12:00 cycle (before noon = yesterday's cycle)
        cycle.CycleId.Should().Be("2026-06-16T12:00");
        cycle.TargetDate.Should().Be(new LocalDate(2026, 6, 17));
    }

    [Fact]
    public void TestCaseG_RebootAt1155And1201DifferentCycles()
    {
        // Reboot at 11:55 (June 17) - belongs to June 16's cycle
        var reboot1155 = CreateHkInstant(2026, 6, 17, 11, 55);
        var cycle1155 = SleepCycleCalculator.GetCurrentCycle(reboot1155);
        cycle1155.CycleId.Should().Be("2026-06-16T12:00");

        // Reboot at 12:01 (June 17) - belongs to June 17's cycle (new cycle)
        var reboot1201 = CreateHkInstant(2026, 6, 17, 12, 1);
        var cycle1201 = SleepCycleCalculator.GetCurrentCycle(reboot1201);
        cycle1201.CycleId.Should().Be("2026-06-17T12:00");

        // First reboot disables June 16's cycle, second doesn't (different cycle)
        SleepCycleCalculator.IsSameCycle(cycle1155.CycleId, cycle1201.CycleId).Should().BeFalse();
    }

    [Fact]
    public void GetTargetDate_ExtractsCorrectDate()
    {
        var cycleId = "2026-06-17T12:00";
        var targetDate = SleepCycleCalculator.GetTargetDate(cycleId);

        targetDate.Should().Be(new LocalDate(2026, 6, 18));
    }

    [Fact]
    public void GetCycleStartTime_ParsesCorrectly()
    {
        var cycleId = "2026-06-17T12:00";
        var startTime = SleepCycleCalculator.GetCycleStartTime(cycleId);

        startTime.LocalDateTime.Date.Should().Be(new LocalDate(2026, 6, 17));
        startTime.LocalDateTime.TimeOfDay.Should().Be(new LocalTime(12, 0));
    }

    [Fact]
    public void IsSameCycle_ExactMatch_ReturnsTrue()
    {
        var cycle1 = "2026-06-17T12:00";
        var cycle2 = "2026-06-17T12:00";

        SleepCycleCalculator.IsSameCycle(cycle1, cycle2).Should().BeTrue();
    }

    [Fact]
    public void IsSameCycle_DifferentCycles_ReturnsFalse()
    {
        var cycle1 = "2026-06-17T12:00";
        var cycle2 = "2026-06-18T12:00";

        SleepCycleCalculator.IsSameCycle(cycle1, cycle2).Should().BeFalse();
    }

    [Fact]
    public void QuotaResetsAtCycleStart()
    {
        var quota = TimeSpan.FromMinutes(90);

        // Get cycle at different times
        var cycle1 = SleepCycleCalculator.GetCurrentCycle(
            CreateHkInstant(2026, 6, 17, 11, 59),
            quota);

        var cycle2 = SleepCycleCalculator.GetCurrentCycle(
            CreateHkInstant(2026, 6, 17, 12, 0),
            quota);

        // Both should have full quota (quota is reset at creation)
        cycle1.QuotaRemaining.Should().Be(quota);
        cycle2.QuotaRemaining.Should().Be(quota);

        // But they're different cycles
        SleepCycleCalculator.IsSameCycle(cycle1.CycleId, cycle2.CycleId).Should().BeFalse();
    }

    private static Instant CreateHkInstant(int year, int month, int day, int hour, int minute)
    {
        var localDateTime = new LocalDateTime(year, month, day, hour, minute);
        var zonedDateTime = HongKongTimeZone.AtLeniently(localDateTime);
        return zonedDateTime.ToInstant();
    }
}
