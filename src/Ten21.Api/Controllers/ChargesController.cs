using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Contracts.Credits;
using Ten21.Api.Contracts.Deposits;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-31/US-33/US-34/US-35 (renamed from ManualChargesController, Sprint 7): CRUD for
/// billable line items posted to a unit's ledger, plus the unit's full financial statement
/// (charges + payments + adjustments + dynamic balance). Nested under a Property, same
/// BOLA/IDOR-safe convention as LeasesController: every action re-checks PropertyId == the
/// route's propertyId rather than trusting a bare {id} lookup. Never scoped to a resident --
/// charges/payments are billed to the unit (tester feedback).
///
/// Business-layer refactor: charge CRUD/lock-rule logic (GetCharges/GetCharge/CreateCharge/
/// UpdateCharge/DeleteCharge/VoidCharge/CreateChargeAdjustment) moved to ChargeService
/// (Ten21.Business). This controller resolves+authorizes the resource
/// (IAuthorizationService.EnsureSameTenantAsync -- an ASP.NET Core-specific concern that
/// deliberately stayed here rather than moving into the framework-agnostic business layer)
/// and delegates the actual operation. GetStatement/GetStatementPdf haven't moved yet -- they
/// span Payments/Credits/Deposits/Refunds too, and are a separate follow-up slice -- so
/// _dbContext is still needed here for those two actions specifically.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/charges")]
public class ChargesController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IPdfService _pdfService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ChargeService _chargeService;

    public ChargesController(
        Ten21DbContext dbContext, IPdfService pdfService, IAuthorizationService authorizationService, ChargeService chargeService)
    {
        _dbContext = dbContext;
        _pdfService = pdfService;
        _authorizationService = authorizationService;
        _chargeService = chargeService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharges(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _chargeService.GetChargesAsync(propertyId, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await _chargeService.FindAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        return Ok(await _chargeService.BuildResponseAsync(charge, cancellationToken));
    }

    /// <summary>
    /// US-33: the unit's full financial statement -- every charge (with adjustments nested
    /// beneath it) and every payment, plus the dynamic running Balance. See
    /// UnitStatementResponse's own comment for the exact formula.
    /// </summary>
    [HttpGet("statement")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetStatement(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);
        return Ok(await BuildStatementAsync(propertyId, cancellationToken));
    }

    /// <summary>
    /// US-40: renders the same statement GetStatement returns as a downloadable/embeddable
    /// PDF (see PaymentsController.GetReceipt's own comment on why "embeddable, not
    /// attachment"), with Charges/Payments filtered to the requested date range -- Balance
    /// itself is always the current snapshot regardless of range, same as the JSON view.
    /// </summary>
    [HttpGet("statement/pdf")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetStatementPdf(
        Guid propertyId, [FromQuery] StatementDateRange range, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);
        var property = await _dbContext.Properties.AsNoTracking().FirstAsync(p => p.Id == propertyId, cancellationToken);
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

        var pdfBytes = _pdfService.GenerateUnitStatement(pdfData);
        return File(pdfBytes, "application/pdf");
    }

    private async Task<UnitStatementResponse> BuildStatementAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var charges = await _dbContext.Charges.AsNoTracking()
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var allAllocations = await _dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        // Ordered client-side, not via OrderBy() in the query -- the SQLite provider (used
        // only by this codebase's in-memory unit tests) can't translate ORDER BY over a
        // DateTimeOffset column; Postgres has no such issue, but ordering after ToListAsync
        // works identically on both and this list is always small (one property's charges).
        var allAdjustments = (await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .OrderBy(a => a.CreatedAt)
            .ToList();
        var allCreditAllocations = (await _dbContext.CreditAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(a => a.AppliedDate)
            .ToList();
        var allDepositAllocations = (await _dbContext.DepositSettlementAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(a => a.AppliedDate)
            .ToList();

        var payments = await _dbContext.PaymentTransactions.AsNoTracking()
            .Include(p => p.Allocations)
            .Where(p => p.PropertyId == propertyId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var deposits = await _dbContext.SecurityDeposits.AsNoTracking()
            .Where(d => d.PropertyId == propertyId)
            .OrderByDescending(d => d.CollectedDate)
            .ToListAsync(cancellationToken);

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
        var residentNamesById = await _dbContext.GetResidentNamesAsync(
            payments.Select(p => p.ResidentProfileId), cancellationToken);
        var paymentResponses = payments.Select(p => new PaymentTransactionResponse(
            p.Id, p.PropertyId, p.ResidentProfileId, residentNamesById.GetValueOrDefault(p.ResidentProfileId, "(unknown resident)"),
            p.PaymentDate, p.AmountPaid, p.TenderType, p.ReferenceNumber, p.Notes, p.UnallocatedAmount,
            p.Status, p.ReversalReason, p.ReallocatedToId,
            p.Allocations.Select(a => new PaymentAllocationSummaryResponse(
                a.ChargeId,
                chargeDescriptionsById.GetValueOrDefault(a.ChargeId, "(unknown charge)"),
                a.AllocatedAmount)).ToList())).ToList();

        var refunds = await _dbContext.RefundTransactions.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.RefundDate)
            .ToListAsync(cancellationToken);
        var refundResidentNamesById = await _dbContext.GetResidentNamesAsync(
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

        var depositResidentNamesById = await _dbContext.GetResidentNamesAsync(
            deposits.Select(d => d.ResidentProfileId), cancellationToken);
        var depositResponses = deposits.Select(d => new SecurityDepositResponse(
            d.Id, d.PropertyId, d.ResidentProfileId, depositResidentNamesById.GetValueOrDefault(d.ResidentProfileId, "(unknown resident)"),
            d.OriginalAmount, d.AmountHeld, d.CollectedDate, d.Status)).ToList();

        // US-39: "if dues exceed held deposit funds... the remaining balance remains on
        // statement under a TerminatedWithBalance account status." Computed, not stored --
        // same pattern as everything else on this statement -- true once at least one deposit
        // on this unit has been Settled and the unit still owes money afterward.
        var accountStatus = deposits.Any(d => d.Status == SecurityDepositStatus.Settled) && balance > 0
            ? AccountStatus.TerminatedWithBalance
            : AccountStatus.Active;

        var transactionLines = BuildTransactionLines(
            charges, allAdjustments, clearedPayments, refunds, allDepositAllocations);

        return new UnitStatementResponse(
            propertyId, balance, availableCredit, accountStatus, chargeStatementItems, paymentResponses, creditResponses,
            refundResponses, depositResponses, transactionLines);
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

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateCharge(
        Guid propertyId, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var response = await _chargeService.CreateAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> UpdateCharge(
        Guid propertyId, Guid id, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await _chargeService.FindAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        return Ok(await _chargeService.UpdateAsync(charge, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> DeleteCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await _chargeService.FindAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        await _chargeService.DeleteAsync(charge, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> VoidCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await _chargeService.FindAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        return Ok(await _chargeService.VoidAsync(charge, cancellationToken));
    }

    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateChargeAdjustment(
        Guid propertyId, Guid id, [FromBody] CreateChargeAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await _chargeService.FindAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        var response = await _chargeService.CreateAdjustmentAsync(charge, request, cancellationToken);

        // Audit Refinement Sprint: was StatusCode(201, ...) -- no Location header, unlike
        // every other create endpoint in this codebase. Adjustments have no standalone
        // GET-by-id of their own (they're only ever read nested under their parent charge,
        // via GetCharge/GetStatement), so this points at the charge they now belong to.
        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = charge.Id }, response);
    }
}
