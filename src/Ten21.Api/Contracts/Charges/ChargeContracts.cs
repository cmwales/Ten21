using Ten21.Api.Contracts.Credits;
using Ten21.Api.Contracts.Deposits;
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

/// <summary>US-37: one later draw-down of a payment's retained credit against a charge --
/// distinct from PaymentAllocationSummaryResponse, which is what the waterfall applied at the
/// moment the payment was originally logged. See CreditAllocation's own class comment.</summary>
public record CreditAllocationResponse(
    Guid Id,
    Guid SourcePaymentTransactionId,
    Guid TargetChargeId,
    string ChargeDescription,
    decimal AppliedAmount,
    DateOnly AppliedDate);

/// <summary>US-33: the whole "lifetime financial statement" for one unit -- charges (with
/// nested adjustments), payments, and refunds, plus the dynamic running Balance
/// (= SumCharges + SumDebits - SumPayments - SumCredits + SumRefunds). Uses total AmountPaid
/// (not just allocated amounts) for the Payments term, so an overpayment can drive Balance
/// negative (a real credit) rather than flooring at the sum of what happened to get allocated.
/// SumRefunds (US-37) is added BACK -- once a resident's held credit has actually been paid
/// out via RefundTransaction, the unit no longer owes it, so Balance has to move back toward
/// zero rather than staying negative forever.
///
/// AvailableCredit (US-37) is a different, more specific number: how much of that overall
/// credit is currently un-drawn-down (sum of PaymentTransaction.UnallocatedAmount) -- what
/// "Apply Credits to Charges" / "Refund Credit Balance" actually operate against. It never
/// changes Balance itself; applying credit just moves money from here into a charge's
/// AllocatedAmount, and refunding it moves money from here out the door (reflected in Balance
/// instead).
///
/// US-39: Balance also subtracts SumDepositSettlements as its own term (never folded into
/// SumPayments -- see SecurityDeposit's own class comment). AccountStatus is computed, not
/// stored: TerminatedWithBalance once at least one Deposits entry is Settled and Balance is
/// still positive afterward (dues exceeded what the deposit could cover).</summary>
public record UnitStatementResponse(
    Guid PropertyId,
    decimal Balance,
    decimal AvailableCredit,
    AccountStatus AccountStatus,
    IReadOnlyList<ChargeStatementItemResponse> Charges,
    IReadOnlyList<PaymentTransactionResponse> Payments,
    IReadOnlyList<CreditAllocationResponse> Credits,
    IReadOnlyList<RefundTransactionResponse> Refunds,
    IReadOnlyList<SecurityDepositResponse> Deposits,
    IReadOnlyList<UnitStatementTransactionLineResponse> TransactionLines);

/// <summary>Refinement Sprint: Charges and PaymentTransactions merged into one chronological
/// (oldest first) timeline with a per-line running Balance, so the statement UI can render a
/// single ledger instead of two separate "Charges" / "Payments" lists. RunningBalance is
/// computed by walking ALL balance-affecting events in date order (charges, adjustments,
/// payments, overpayment refunds, deposit settlements -- the same terms UnitStatementResponse's
/// own Balance formula uses) and snapshotting the cumulative total after each one, but only
/// Charge/Payment events are surfaced here -- adjustments/credits/refunds/deposits already
/// have their own nested/sectioned rendering elsewhere on the statement, so this stays a
/// simple two-type timeline while still landing on the mathematically correct number at each
/// point. ReferenceId is the Charge.Id or PaymentTransaction.Id so the UI can look up the
/// full rich object (adjustments, allocations, actions) it already has loaded rather than
/// duplicating that shape here.</summary>
public record UnitStatementTransactionLineResponse(
    string Type,
    DateOnly Date,
    Guid ReferenceId,
    decimal RunningBalance);
