using Ten21.Domain.Enums;

namespace Ten21.Business.Payments;

/// <summary>
/// Business-layer refactor: relocated from Ten21.Api.Contracts.Charges (the payment-specific
/// records living alongside the charge/statement ones there) so PaymentService can return
/// these directly. Ten21.Api.Contracts.Charges.ChargeContracts.cs's statement-building records
/// (UnitStatementResponse, etc.) still reference PaymentTransactionResponse/
/// PaymentAllocationSummaryResponse via a `using Ten21.Business.Payments;`.
///
/// US-34: captures a manually-received payment; the amount is applied automatically via the
/// statutory waterfall (LateFee/Legal, then BaseRent, then AddOn/SpecialAssessment -- see
/// Charge.DefaultAllocationPriorityFor), never chosen per-charge by the PM. ResidentProfileId
/// is required -- see PaymentTransaction's own class comment for why the unit-only "we don't
/// care who paid" call didn't hold up: an overpayment or a pre-charge payment becomes a credit
/// owed to a specific person, who needs to stay identifiable for a refund even after they
/// transfer units or a co-tenant moves out.
/// </summary>
public record LogPaymentRequest(
    Guid ResidentProfileId,
    DateOnly PaymentDate,
    decimal AmountPaid,
    TenderType TenderType,
    string? ReferenceNumber,
    string? Notes);

public record PaymentAllocationSummaryResponse(
    Guid ChargeId,
    string ChargeDescription,
    decimal AllocatedAmount);

/// <summary>UnallocatedAmount (US-37) is this payment's own remaining retained credit -- see
/// PaymentTransaction's own class comment. Decreases as CreditAllocations draw it down or a
/// RefundTransaction pays it out; the sum of this across a unit's payments is
/// UnitStatementResponse.AvailableCredit. Status/ReversalReason/ReallocatedToId (US-38) --
/// see PaymentTransaction's own class comment for the reversal/reallocation mechanics; a
/// Reversed payment's Allocations always comes back empty (un-linked, not deleted).</summary>
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
    decimal UnallocatedAmount,
    PaymentTransactionStatus Status,
    string? ReversalReason,
    Guid? ReallocatedToId,
    IReadOnlyList<PaymentAllocationSummaryResponse> Allocations);

/// <summary>US-38: "Reverse Payment" -- an NSF/bounced payment. ReversalReason is mandatory,
/// same audit-explanation convention as ChargeAdjustment.Reason. The optional NSF fee the
/// acceptance criteria calls for is deliberately NOT bundled into this request -- it's just a
/// normal Charge (Category=LateFee) posted afterward via the existing CreateCharge endpoint,
/// so this endpoint doesn't have to duplicate charge-creation logic for a one-off case.</summary>
public record ReversePaymentRequest(string ReversalReason);

/// <summary>US-38: "Reallocate Payment" -- a cross-property posting error. Reverses this
/// payment (same mechanics as ReversePaymentRequest) and, atomically, creates a brand-new
/// PaymentTransaction under the correct property/resident, running the statutory waterfall
/// against it exactly like a fresh LogPayment. ReversalReason doubles as the cross-reference
/// note stamped on both the reversed original and the new payment's Notes.</summary>
public record ReallocatePaymentRequest(
    Guid TargetPropertyId,
    Guid TargetResidentProfileId,
    string ReversalReason);
