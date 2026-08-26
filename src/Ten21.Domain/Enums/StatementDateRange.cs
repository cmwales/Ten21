namespace Ten21.Domain.Enums;

/// <summary>US-40: filters a PDF statement's Charges/Payments to a period -- Balance itself
/// is always the current snapshot regardless of range.</summary>
public enum StatementDateRange
{
    Lifetime,
    YearToDate,
    Last12Months,
}
