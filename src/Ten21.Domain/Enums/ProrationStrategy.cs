namespace Ten21.Domain.Enums;

/// <summary>US-44: how the first charge generated for a template is handled when
/// EffectiveStartDate falls after that period's regular due date has already passed.
/// FullAmount -- charge the full Amount regardless. ZeroFirstMonth -- skip generating a
/// charge for that first partial period entirely. ProRateByDays -- charge a daily-rate
/// portion covering EffectiveStartDate through the day before the next regular due date.</summary>
public enum ProrationStrategy
{
    FullAmount,
    ZeroFirstMonth,
    ProRateByDays,
}
