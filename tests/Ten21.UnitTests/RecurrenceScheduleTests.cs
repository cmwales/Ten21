using Ten21.Business.Billing;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-44 (Sprint 9): pure date-math tests for the generation engine's
/// due-date/clamping logic -- no DbContext involved, see BillingCycleServiceTests for the
/// end-to-end generation/idempotency/transaction behavior.</summary>
public class RecurrenceScheduleTests
{
    private static LeaseRecurringCharge Template(
        RecurrencePattern pattern,
        DateOnly effectiveStartDate,
        int? dueDayOfMonth = null,
        int recurrenceInterval = 1,
        DayOfWeek? targetDayOfWeek = null,
        int? secondaryDueDay = null) => new()
    {
        Id = Guid.NewGuid(),
        LeaseId = Guid.NewGuid(),
        ChargeName = "Test Charge",
        Category = ChargeCategory.BaseRent,
        Amount = 100m,
        RecurrencePattern = pattern,
        RecurrenceInterval = recurrenceInterval,
        DueDayOfMonth = dueDayOfMonth,
        TargetDayOfWeek = targetDayOfWeek,
        SecondaryDueDay = secondaryDueDay,
        EndStrategy = EndStrategy.Indefinite,
        EffectiveStartDate = effectiveStartDate,
        ProrationStrategy = ProrationStrategy.FullAmount,
    };

    [Fact]
    public void IsDueOn_Monthly_ClampsTo28thInFebruary_WhenDueDayIs31()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 31);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2027, 2, 28)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2027, 2, 27)));
    }

    [Fact]
    public void IsDueOn_Monthly_ClampsTo29thInFebruary_OnALeapYear_WhenDueDayIs31()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 31);

        // 2028 is a leap year.
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void IsDueOn_Monthly_DoesNotMutateStoredDueDayOfMonth()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 31);

        RecurrenceSchedule.IsDueOn(template, new DateOnly(2027, 2, 28));

        Assert.Equal(31, template.DueDayOfMonth);
    }

    [Fact]
    public void IsDueOn_Monthly_MatchesTheStoredDayDirectly_InA31DayMonth()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 31);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void IsDueOn_Monthly_ReturnsFalse_BeforeEffectiveStartDate()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 6, 1), dueDayOfMonth: 1);

        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void IsDueOn_Monthly_RespectsRecurrenceInterval_EveryOtherMonth()
    {
        var template = Template(RecurrencePattern.Monthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 1, recurrenceInterval: 2);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 1, 1)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 2, 1)));
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void IsDueOn_SemiMonthly_MatchesEitherDueDay()
    {
        var template = Template(
            RecurrencePattern.SemiMonthly, new DateOnly(2026, 1, 1), dueDayOfMonth: 1, secondaryDueDay: 15);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 3, 1)));
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 3, 15)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 3, 16)));
    }

    [Fact]
    public void IsDueOn_Weekly_MatchesTargetDayOfWeek_EveryWeek()
    {
        // 2026-08-31 is a Monday.
        var template = Template(RecurrencePattern.Weekly, new DateOnly(2026, 8, 31), targetDayOfWeek: DayOfWeek.Monday);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 8, 31)));
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 7)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 8)));
    }

    [Fact]
    public void IsDueOn_BiWeekly_SkipsTheInterveningWeek()
    {
        var template = Template(RecurrencePattern.BiWeekly, new DateOnly(2026, 8, 31), targetDayOfWeek: DayOfWeek.Monday);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 8, 31)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 7)));
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 14)));
    }

    [Fact]
    public void IsDueOn_Daily_RespectsRecurrenceInterval()
    {
        var template = Template(RecurrencePattern.Daily, new DateOnly(2026, 9, 1), recurrenceInterval: 3);

        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 1)));
        Assert.False(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 2)));
        Assert.True(RecurrenceSchedule.IsDueOn(template, new DateOnly(2026, 9, 4)));
    }
}
