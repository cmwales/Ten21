using Ten21.Domain.Enums;

namespace Ten21.Business.Charges;

/// <summary>
/// Business-layer refactor: relocated from Ten21.Api.Contracts.Charges so ChargeService can
/// return these directly without Api's contracts project owning types the business layer
/// itself produces. Ten21.Api.Contracts.Charges.ChargeContracts.cs still holds the
/// statement/payment-related records that reference these (ChargeStatementItemResponse,
/// etc.) via a `using Ten21.Business.Charges;` -- only the charge-CRUD-specific records
/// moved, not the whole file, since the statement-building logic itself hasn't moved yet.
///
/// Renamed/extended from UpsertManualChargeRequest (Sprint 7). AllocationPriority is never
/// client-supplied -- it's always derived server-side from Category via
/// Charge.DefaultAllocationPriorityFor (see that method's own comment on why there's no
/// override yet).
/// </summary>
public record UpsertChargeRequest(
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode,
    ChargeCategory Category,
    string? Notes = null);

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
    bool IsLocked,
    string? Notes);

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
