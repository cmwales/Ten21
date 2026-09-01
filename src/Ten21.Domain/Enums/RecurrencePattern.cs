namespace Ten21.Domain.Enums;

/// <summary>US-44: how often a LeaseRecurringCharge template generates a Charge. Monthly
/// (the common case -- base rent, most add-ons) uses DueDayOfMonth with runtime clamping.
/// Weekly/BiWeekly use TargetDayOfWeek. SemiMonthly uses both DueDayOfMonth and
/// SecondaryDueDay. Daily/Custom step by RecurrenceInterval days from EffectiveStartDate.</summary>
public enum RecurrencePattern
{
    Daily,
    Weekly,
    BiWeekly,
    SemiMonthly,
    Monthly,
    Custom,
}
