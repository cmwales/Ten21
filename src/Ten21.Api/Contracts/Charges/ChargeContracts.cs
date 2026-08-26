using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Charges;

/// <summary>Renamed/extended from UpsertManualChargeRequest (Sprint 7). AllocationPriority
/// is never client-supplied -- it's always derived server-side from Category via
/// Charge.DefaultAllocationPriorityFor (see that method's own comment on why there's no
/// override yet).</summary>
public record UpsertChargeRequest(
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode,
    ChargeCategory Category);

/// <summary>AllocatedAmount/PaymentStatus/IsLocked are computed at read time from
/// PaymentAllocation + ChargeAdjustment rows, never stored on Charge itself -- see Charge's
/// own class comment.</summary>
public record ChargeResponse(
    Guid Id,
    Guid PropertyId,
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode,
    ChargeCategory Category,
    ChargeLifecycleStatus Status,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    ChargePaymentStatus PaymentStatus,
    bool IsLocked);

public record ChargeAdjustmentResponse(
    Guid Id,
    AdjustmentType AdjustmentType,
    decimal Amount,
    string Reason,
    DateTimeOffset CreatedAt);

/// <summary>US-35: the ONLY way to change what's owed on a locked charge (one with a payment
/// already allocated) -- Reason is mandatory because this is a financial correction, not a
/// convenience edit; unlike UpdateCharge/DeleteCharge/VoidCharge, this works on locked charges
/// on purpose (that's the whole point of ChargeAdjustment existing) and also works on unlocked
/// ones (e.g. a goodwill credit that shouldn't touch the charge's own stored Amount).</summary>
public record CreateChargeAdjustmentRequest(
    AdjustmentType AdjustmentType,
    decimal Amount,
    string Reason);

/// <summary>US-33: one charge row on the unit statement, with its adjustments nested
/// directly beneath it (per the acceptance criteria's "adjustments render indented beneath
/// their target charge row").</summary>
public record ChargeStatementItemResponse(
    ChargeResponse Charge,
    IReadOnlyList<ChargeAdjustmentResponse> Adjustments);

public record PaymentAllocationSummaryResponse(
    Guid ChargeId,
    string ChargeDescription,
    decimal AllocatedAmount);

/// <summary>US-34: captures a manually-received payment; the amount is applied automatically
/// via the statutory waterfall (LateFee/Legal, then BaseRent, then AddOn/SpecialAssessment --
/// see Charge.DefaultAllocationPriorityFor), never chosen per-charge by the PM.
/// ResidentProfileId is required (fix, post-US-34) -- see PaymentTransaction's own class
/// comment for why the unit-only "we don't care who paid" call didn't hold up: an overpayment
/// or a pre-charge payment becomes a credit owed to a specific person, who needs to stay
/// identifiable for a refund even after they transfer units or a co-tenant moves out.</summary>
public record LogPaymentRequest(
    Guid ResidentProfileId,
    DateOnly PaymentDate,
    decimal AmountPaid,
    TenderType TenderType,
    string? ReferenceNumber,
    string? Notes);

public record PaymentTransactionResponse(
    Guid Id,
    Guid PropertyId,
    Guid ResidentProfileId,
    string ResidentName,
    DateOnly PaymentDate,
    decimal AmountPaid,
    TenderType TenderType,
    string? ReferenceNumber,
    string? Notes,
    IReadOnlyList<PaymentAllocationSummaryResponse> Allocations);

/// <summary>US-33: the whole "lifetime financial statement" for one unit -- charges (with
/// nested adjustments) and payments, plus the dynamic running Balance
/// (= SumCharges + SumDebits - SumPayments - SumCredits). Uses total AmountPaid (not just
/// allocated amounts) for the Payments term, so an overpayment can drive Balance negative
/// (a real credit) rather than flooring at the sum of what happened to get allocated.</summary>
public record UnitStatementResponse(
    Guid PropertyId,
    decimal Balance,
    IReadOnlyList<ChargeStatementItemResponse> Charges,
    IReadOnlyList<PaymentTransactionResponse> Payments);
