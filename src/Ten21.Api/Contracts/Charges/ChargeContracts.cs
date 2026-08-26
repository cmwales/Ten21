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

public record PaymentTransactionResponse(
    Guid Id,
    Guid PropertyId,
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
