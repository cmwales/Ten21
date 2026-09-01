namespace Ten21.Domain.Enums;

/// <summary>US-44: how a LeaseRecurringCharge template's active window ends. Indefinite --
/// no end boundary, ignores EffectiveEndDate. FixedDate -- bounded by the template's own
/// stored EffectiveEndDate. LeaseAligned -- bounded by the parent Lease's EndDate,
/// evaluated dynamically (not copied onto the template) so a lease renewal automatically
/// extends every LeaseAligned template without needing its own update.</summary>
public enum EndStrategy
{
    Indefinite,
    FixedDate,
    LeaseAligned,
}
