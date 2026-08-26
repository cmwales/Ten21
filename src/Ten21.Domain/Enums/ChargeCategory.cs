namespace Ten21.Domain.Enums;

/// <summary>US-34: drives the statutory payment-allocation waterfall order via
/// Charge.DefaultAllocationPriority(this) -- LateFee/Legal are satisfied before BaseRent,
/// which is satisfied before AddOn/SpecialAssessment. See Charge.cs for the actual priority
/// mapping.</summary>
public enum ChargeCategory
{
    LateFee,
    Legal,
    BaseRent,
    AddOn,
    SpecialAssessment,
}
