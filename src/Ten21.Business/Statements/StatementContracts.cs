using Ten21.Application.Ledger;
using Ten21.Business.Charges;
using Ten21.Business.Payments;
using Ten21.Domain.Enums;

namespace Ten21.Business.Statements;

/// <summary>
/// Business-layer refactor: relocated from Ten21.Api.Contracts.Charges so StatementService
/// can return these directly. This is the cross-cutting record set -- unlike Charges/
/// Payments' own contracts, these reference RefundTransactionResponse/SecurityDepositResponse
/// too, which is why those two live in the shared Ten21.Application.Ledger rather than in
/// Ten21.Business.Credits/Deposits (neither of which exists yet -- CreditsController/
/// DepositsController/RefundsController haven't been migrated in this pass and still do their
/// own direct database access; that's a known follow-up, not an oversight).
///
/// US-33: one charge row on the unit statement, with its adjustments nested directly beneath
/// it (per the acceptance criteria's "adjustments render indented beneath their target charge
/// row").
/// </summary>
public record ChargeStatementItemResponse(
    ChargeResponse Charge,
    IReadOnlyList<ChargeAdjustmentResponse> Adjustments);

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
