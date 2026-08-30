using Ten21.Application.Abstractions;
using Ten21.Application.Ledger;
using Ten21.Business.Charges;
using Ten21.Business.Payments;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;

namespace Ten21.Business.Statements;

/// <summary>
/// Business-layer refactor: extracted from ChargesController.BuildStatementAsync/
/// BuildTransactionLines/GetStatementPdf. Depends on ChargeService (for the same
/// ChargeResponse-shaping rule GetCharges/GetCharge use, so a charge looks identical whether
/// you're looking at it standalone or on the statement) rather than duplicating that logic.
/// No interface -- same reasoning as every other class in this project.
/// </summary>
public class StatementService
{
    private readonly StatementRepository _repository;
    private readonly ChargeService _chargeService;
    private readonly IPdfService _pdfService;

    public StatementService(StatementRepository repository, ChargeService chargeService, IPdfService pdfService)
    {
        _repository = repository;
        _chargeService = chargeService;
        _pdfService = pdfService;
    }

    /// <summary>
    /// US-33: the unit's full financial statement -- every charge (with adjustments nested
    /// beneath it) and every payment, plus the dynamic running Balance.
    /// </summary>
    public async Task<UnitStatementResponse> BuildStatementAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        await _repository.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _repository.ListChargesAsync(propertyId, cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var allAllocations = await _repository.ListAllocationsForChargesAsync(chargeIds, cancellationToken);
        // Ordered client-side, not via OrderBy() in the query -- the SQLite provider (used
        // only by this codebase's in-memory unit tests) can't translate ORDER BY over a
        // DateTimeOffset column; Postgres has no such issue, but ordering after ToListAsync
        // works identically on both and this list is always small (one property's charges).
        var allAdjustments = (await _repository.ListAdjustmentsForChargesAsync(chargeIds, cancellationToken))
            .OrderBy(a => a.CreatedAt)
            .ToList();
        var allCreditAllocations = (await _repository.ListCreditAllocationsForChargesAsync(chargeIds, cancellationToken))
            .OrderByDescending(a => a.AppliedDate)
            .ToList();
        var allDepositAllocations = (await _repository.ListDepositAllocationsForChargesAsync(chargeIds, cancellationToken))
            .OrderByDescending(a => a.AppliedDate)
            .ToList();

        var payments = await _repository.ListPaymentsAsync(propertyId, cancellationToken);
        var deposits = await _repository.ListDepositsAsync(propertyId, cancellationToken);

        var chargeDescriptionsByIdForCredits = charges.ToDictionary(c => c.Id, c => c.Description);
        var creditResponses = allCreditAllocations.Select(a => new CreditAllocationResponse(
            a.Id, a.SourcePaymentTransactionId, a.TargetChargeId,
            chargeDescriptionsByIdForCredits.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        var chargeStatementItems = charges.Select(charge =>
        {
            var allocatedAmount = allAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + allCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + allDepositAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var adjustmentsForCharge = allAdjustments.Where(a => a.TargetChargeId == charge.Id).ToList();
            var chargeResponse = _chargeService.BuildResponse(charge, allocatedAmount, adjustmentsForCharge);
            var adjustmentResponses = adjustmentsForCharge
                .Select(a => new ChargeAdjustmentResponse(a.Id, a.AdjustmentType, a.Amount, a.Reason, a.CreatedAt))
                .ToList();
            return new ChargeStatementItemResponse(chargeResponse, adjustmentResponses);
        }).ToList();

        var chargeDescriptionsById = charges.ToDictionary(c => c.Id, c => c.Description);
        var residentNamesById = await _repository.GetResidentNamesAsync(
            payments.Select(p => p.ResidentProfileId), cancellationToken);
        var paymentResponses = payments.Select(p => new PaymentTransactionResponse(
            p.Id, p.PropertyId, p.ResidentProfileId, residentNamesById.GetValueOrDefault(p.ResidentProfileId, "(unknown resident)"),
            p.PaymentDate, p.AmountPaid, p.TenderType, p.ReferenceNumber, p.Notes, p.UnallocatedAmount,
            p.Status, p.ReversalReason, p.ReallocatedToId,
            p.Allocations.Select(a => new PaymentAllocationSummaryResponse(
                a.ChargeId,
                chargeDescriptionsById.GetValueOrDefault(a.ChargeId, "(unknown charge)"),
                a.AllocatedAmount)).ToList())).ToList();

        var refunds = await _repository.ListRefundsAsync(propertyId, cancellationToken);
        var refundResidentNamesById = await _repository.GetResidentNamesAsync(
            refunds.Select(r => r.ResidentProfileId), cancellationToken);
        var refundResponses = refunds.Select(r => new RefundTransactionResponse(
            r.Id, r.ResidentProfileId, refundResidentNamesById.GetValueOrDefault(r.ResidentProfileId, "(unknown resident)"),
            r.PropertyId, r.Amount, r.RefundDate, r.TenderType, r.ReferenceNumber, r.Reason, r.CreatedAt)).ToList();

        // See UnitStatementResponse's own comment for why Payments uses total AmountPaid
        // (not just allocated amounts) -- an overpayment should show as a credit (negative
        // balance), not just floor debt at zero. Refunds are added BACK (US-37) -- but ONLY
        // OverpaymentRefund ones: that refund is reversing money that WAS already counted in
        // SumPayments above, so it has to counteract that pull. A DepositReturn refund
        // (US-39) never came from SumPayments in the first place (deposit money is
        // deliberately excluded from it -- see below), so adding it here would double-count
        // it and inflate Balance. Reversed payments (US-38) are excluded entirely -- a
        // bounced/misposted payment never really cleared, so it shouldn't count toward money
        // the unit has received. Deposit settlements are subtracted as their OWN term,
        // deliberately never folded into SumPayments -- escrow money satisfying a charge is a
        // liability transfer, not rental income received.
        var clearedPayments = payments.Where(p => p.Status != PaymentTransactionStatus.Reversed).ToList();
        var sumActiveCharges = charges.Where(c => c.Status == ChargeLifecycleStatus.Active).Sum(c => c.Amount);
        var sumDebits = allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.DebitAdjustment).Sum(a => a.Amount);
        var sumCredits = allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.CreditAdjustment).Sum(a => a.Amount);
        var sumPayments = clearedPayments.Sum(p => p.AmountPaid);
        var sumOverpaymentRefunds = refunds.Where(r => r.Reason == RefundReason.OverpaymentRefund).Sum(r => r.Amount);
        var sumDepositSettlements = allDepositAllocations.Sum(a => a.AppliedAmount);
        var balance = sumActiveCharges + sumDebits - sumPayments - sumCredits + sumOverpaymentRefunds - sumDepositSettlements;

        // US-37: AvailableCredit is distinct from Balance -- Balance already reflects an
        // overpayment as a credit implicitly (via subtracting the full AmountPaid above), but
        // AvailableCredit is the more specific "how much of that is still sitting un-drawn-down
        // right now" figure that the "Apply Credits to Charges" / "Refund Credit Balance"
        // actions actually operate against. Applying credit to a charge moves money from here
        // into a charge's AllocatedAmount; refunding it moves money out the door (reflected in
        // Balance above instead) -- neither changes Balance directly.
        var availableCredit = clearedPayments.Sum(p => p.UnallocatedAmount);

        var depositResidentNamesById = await _repository.GetResidentNamesAsync(
            deposits.Select(d => d.ResidentProfileId), cancellationToken);
        var depositResponses = deposits.Select(d => new SecurityDepositResponse(
            d.Id, d.PropertyId, d.ResidentProfileId, depositResidentNamesById.GetValueOrDefault(d.ResidentProfileId, "(unknown resident)"),
            d.OriginalAmount, d.AmountHeld, d.CollectedDate, d.Status)).ToList();

        // US-39: "if dues exceed held deposit funds... the remaining balance remains on
        // statement under a TerminatedWithBalance account status." Computed, not stored --
        // true once at least one deposit on this unit has been Settled and the unit still
        // owes money afterward.
        var accountStatus = deposits.Any(d => d.Status == SecurityDepositStatus.Settled) && balance > 0
            ? AccountStatus.TerminatedWithBalance
            : AccountStatus.Active;

        var transactionLines = BuildTransactionLines(
            charges, allAdjustments, clearedPayments, refunds, allDepositAllocations);

        return new UnitStatementResponse(
            propertyId, balance, availableCredit, accountStatus, chargeStatementItems, paymentResponses, creditResponses,
            refundResponses, depositResponses, transactionLines);
    }

    /// <summary>
    /// US-40: renders the same statement BuildStatementAsync returns as PDF bytes, with
    /// Charges/Payments filtered to the requested date range -- Balance itself is always the
    /// current snapshot regardless of range, same as the JSON view.
    /// </summary>
    public async Task<byte[]> BuildStatementPdfAsync(Guid propertyId, StatementDateRange range, CancellationToken cancellationToken)
    {
        await _repository.EnsurePropertyExistsAsync(propertyId, cancellationToken);
        var property = await _repository.GetPropertyAsync(propertyId, cancellationToken);
        var statement = await BuildStatementAsync(propertyId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (rangeStart, rangeLabel) = range switch
        {
            StatementDateRange.YearToDate => (new DateOnly(today.Year, 1, 1), "Year-to-Date"),
            StatementDateRange.Last12Months => (today.AddMonths(-12), "Last 12 Months"),
            _ => ((DateOnly?)null, "Lifetime"),
        };

        var chargeLines = statement.Charges
            .Where(c => rangeStart is null || c.Charge.DueDate >= rangeStart)
            .Select(c => new UnitStatementPdfChargeLine(
                c.Charge.Description, c.Charge.Category.ToString(), c.Charge.DueDate, c.Charge.Amount, c.Charge.PaymentStatus.ToString()))
            .ToList();
        var paymentLines = statement.Payments
            .Where(p => rangeStart is null || p.PaymentDate >= rangeStart)
            .Select(p => new UnitStatementPdfPaymentLine(p.PaymentDate, p.TenderType.ToString(), p.ResidentName, p.AmountPaid))
            .ToList();

        var pdfData = new UnitStatementPdfData(
            property.Name, property.UnitIdentifier, rangeLabel, statement.Balance, chargeLines, paymentLines);

        return _pdfService.GenerateUnitStatement(pdfData);
    }

    /// <summary>Directive 3 (Refinement Sprint): walks every balance-affecting event in
    /// chronological order -- the same terms BuildStatementAsync's own Balance formula sums,
    /// just per-event instead of all at once -- and snapshots the cumulative running balance
    /// after each one. Only Charge/Payment events are returned (adjustments/refunds/deposit
    /// settlements already render in their own sections), but their RunningBalance correctly
    /// reflects any of those other events that happened in between, because the walk itself
    /// includes all five event kinds before filtering the output.</summary>
    private static List<UnitStatementTransactionLineResponse> BuildTransactionLines(
        List<Charge> charges,
        List<ChargeAdjustment> adjustments,
        List<PaymentTransaction> clearedPayments,
        List<RefundTransaction> refunds,
        List<DepositSettlementAllocation> depositAllocations)
    {
        var events = new List<(DateOnly Date, string Type, Guid ReferenceId, decimal Delta, bool Surface)>();

        foreach (var charge in charges.Where(c => c.Status == ChargeLifecycleStatus.Active))
        {
            events.Add((charge.DueDate, "Charge", charge.Id, charge.Amount, Surface: true));
        }

        foreach (var adjustment in adjustments)
        {
            var delta = ChargeLedgerMath.NetAdjustment([adjustment]);
            events.Add((DateOnly.FromDateTime(adjustment.CreatedAt.UtcDateTime), "Adjustment", adjustment.Id, delta, Surface: false));
        }

        foreach (var payment in clearedPayments)
        {
            events.Add((payment.PaymentDate, "Payment", payment.Id, -payment.AmountPaid, Surface: true));
        }

        foreach (var refund in refunds.Where(r => r.Reason == RefundReason.OverpaymentRefund))
        {
            events.Add((refund.RefundDate, "Refund", refund.Id, refund.Amount, Surface: false));
        }

        foreach (var depositAllocation in depositAllocations)
        {
            events.Add((depositAllocation.AppliedDate, "DepositSettlement", depositAllocation.Id, -depositAllocation.AppliedAmount, Surface: false));
        }

        var runningBalance = 0m;
        var lines = new List<UnitStatementTransactionLineResponse>();
        foreach (var evt in events.OrderBy(e => e.Date))
        {
            runningBalance += evt.Delta;
            if (evt.Surface)
            {
                lines.Add(new UnitStatementTransactionLineResponse(evt.Type, evt.Date, evt.ReferenceId, runningBalance));
            }
        }

        return lines;
    }
}
