using Ten21.Domain.Entities;
using Ten21.Domain.Enums;

namespace Ten21.Business.Billing;

/// <summary>
/// US-44 (Sprint 9): pure date-math deciding whether a LeaseRecurringCharge template is
/// due on a given execution date. Kept static/pure (no DbContext) so it's directly unit
/// testable without a database -- BillingCycleService is the only caller.
///
/// Monthly/SemiMonthly clamping is the acceptance criteria's named requirement:
/// min(DueDayOfMonth, DaysInMonth(year, month)), computed fresh every call -- the stored
/// DueDayOfMonth is never mutated, so "the 31st" keeps meaning "the 31st" even though it
/// resolves to the 28th/29th in February.
/// </summary>
public static class RecurrenceSchedule
{
    public static bool IsDueOn(LeaseRecurringCharge template, DateOnly executionDate)
    {
        if (executionDate < template.EffectiveStartDate)
        {
            return false;
        }

        return template.RecurrencePattern switch
        {
            RecurrencePattern.Monthly => IsMonthlyOccurrence(
                template.EffectiveStartDate, RequireDueDay(template), template.RecurrenceInterval, executionDate),
            RecurrencePattern.SemiMonthly => IsMonthlyOccurrence(
                    template.EffectiveStartDate, RequireDueDay(template), 1, executionDate)
                || (template.SecondaryDueDay is { } secondary
                    && IsMonthlyOccurrence(template.EffectiveStartDate, secondary, 1, executionDate)),
            RecurrencePattern.Weekly => IsWeeklyOccurrence(template, executionDate, weekStepDays: 7),
            RecurrencePattern.BiWeekly => IsWeeklyOccurrence(template, executionDate, weekStepDays: 14),
            RecurrencePattern.Daily or RecurrencePattern.Custom => IsDailyOccurrence(template, executionDate),
            _ => throw new ArgumentOutOfRangeException(nameof(template.RecurrencePattern)),
        };
    }

    /// <summary>Used by ProrationStrategy.ProRateByDays to know how many days are "one
    /// period" for the pattern in question, so a partial first period can be billed as a
    /// fraction of it.</summary>
    public static int PeriodLengthDays(LeaseRecurringCharge template, DateOnly occurrenceDate) =>
        template.RecurrencePattern switch
        {
            RecurrencePattern.Monthly or RecurrencePattern.SemiMonthly =>
                DateTime.DaysInMonth(occurrenceDate.Year, occurrenceDate.Month),
            RecurrencePattern.Weekly => 7 * template.RecurrenceInterval,
            RecurrencePattern.BiWeekly => 14 * template.RecurrenceInterval,
            RecurrencePattern.Daily or RecurrencePattern.Custom => Math.Max(1, template.RecurrenceInterval),
            _ => throw new ArgumentOutOfRangeException(nameof(template.RecurrencePattern)),
        };

    /// <summary>The clamping requirement: a stored due day of 31 resolves to the last real
    /// day of a short month (Feb 28/29, Apr/Jun/Sep/Nov 30) instead of never matching.</summary>
    private static bool IsMonthlyOccurrence(DateOnly effectiveStartDate, int dueDay, int intervalMonths, DateOnly executionDate)
    {
        var clampedDay = Math.Min(dueDay, DateTime.DaysInMonth(executionDate.Year, executionDate.Month));
        if (executionDate.Day != clampedDay)
        {
            return false;
        }

        var monthsSinceStart = ((executionDate.Year - effectiveStartDate.Year) * 12) + executionDate.Month - effectiveStartDate.Month;
        return monthsSinceStart >= 0 && monthsSinceStart % Math.Max(1, intervalMonths) == 0;
    }

    private static bool IsWeeklyOccurrence(LeaseRecurringCharge template, DateOnly executionDate, int weekStepDays)
    {
        if (template.TargetDayOfWeek is not { } targetDay)
        {
            return false;
        }

        var daysToFirstTarget = ((int)targetDay - (int)template.EffectiveStartDate.DayOfWeek + 7) % 7;
        var firstOccurrence = template.EffectiveStartDate.AddDays(daysToFirstTarget);
        if (executionDate < firstOccurrence)
        {
            return false;
        }

        var daysSinceFirst = executionDate.DayNumber - firstOccurrence.DayNumber;
        var stepDays = weekStepDays * Math.Max(1, template.RecurrenceInterval);
        return daysSinceFirst % stepDays == 0;
    }

    private static bool IsDailyOccurrence(LeaseRecurringCharge template, DateOnly executionDate)
    {
        var daysSinceStart = executionDate.DayNumber - template.EffectiveStartDate.DayNumber;
        return daysSinceStart % Math.Max(1, template.RecurrenceInterval) == 0;
    }

    private static int RequireDueDay(LeaseRecurringCharge template) =>
        template.DueDayOfMonth ?? throw new InvalidOperationException(
            $"LeaseRecurringCharge '{template.Id}' has RecurrencePattern={template.RecurrencePattern} but no DueDayOfMonth.");
}
