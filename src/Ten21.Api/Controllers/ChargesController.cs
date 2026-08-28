using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Contracts.Credits;
using Ten21.Api.Contracts.Deposits;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
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
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/charges")]
public class ChargesController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;
    private readonly IPdfService _pdfService;
    private readonly IAuthorizationService _authorizationService;

    public ChargesController(
        Ten21DbContext dbContext, IInputSanitizer sanitizer, IPdfService pdfService, IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _pdfService = pdfService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Audit Refinement Sprint: previously called BuildChargeResponseAsync per charge, each
    /// of which issued 4 queries of its own (3 for allocated amount, 1 for adjustments) --
    /// unbounded by pagination, so a property with 50 charges issued ~200 queries for one
    /// GET. Now batch-loads allocations/adjustments for every charge on the property ONCE and
    /// groups them in memory, the same pattern BuildStatementAsync already used correctly.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharges(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _dbContext.Charges.AsNoTracking()
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var allocatedAmountsByChargeId = await GetAllocatedAmountsByChargeAsync(chargeIds, cancellationToken);
        var adjustmentsByChargeId = (await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.TargetChargeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var responses = charges
            .Select(charge => BuildChargeResponse(
                charge,
                allocatedAmountsByChargeId.GetValueOrDefault(charge.Id, 0m),
                adjustmentsByChargeId.GetValueOrDefault(charge.Id, [])))
            .ToList();

        return Ok(responses);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await FindChargeAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        return Ok(await BuildChargeResponseAsync(charge, cancellationToken));
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
            var chargeResponse = BuildChargeResponse(charge, allocatedAmount, adjustmentsForCharge);
            return new ChargeStatementItemResponse(chargeResponse, adjustmentsForCharge.Select(ToAdjustmentResponse).ToList());
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
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);
        var fields = ValidateAndSanitize(request);

        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Description = fields.Description,
            Amount = request.Amount,
            DueDate = request.DueDate,
            AccountingCode = fields.AccountingCode,
            Category = request.Category,
            Notes = fields.Notes,
            AllocationPriority = Charge.DefaultAllocationPriorityFor(request.Category),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Charges.Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = charge.Id }, BuildChargeResponse(charge, 0m, []));
    }

    /// <summary>US-35: only unlocked (zero dollars allocated) charges can be edited directly
    /// -- once a payment has been applied, the base Amount is permanently locked and
    /// corrections must go through a ChargeAdjustment instead.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> UpdateCharge(
        Guid propertyId, Guid id, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await FindChargeAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        if (allocatedAmount > 0)
        {
            throw new ConflictException(
                "This charge already has payments applied and its amount is locked. Post a credit or debit adjustment instead.");
        }

        charge.Description = fields.Description;
        charge.Amount = request.Amount;
        charge.DueDate = request.DueDate;
        charge.AccountingCode = fields.AccountingCode;
        charge.Category = request.Category;
        charge.Notes = fields.Notes;
        charge.AllocationPriority = Charge.DefaultAllocationPriorityFor(request.Category);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(BuildChargeResponse(charge, 0m, []));
    }

    /// <summary>Always a soft delete (Charge is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- a posted charge is a financial record,
    /// never hard-erased. Same lock rule as UpdateCharge: only an unpaid charge can be
    /// removed outright.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> DeleteCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await FindChargeAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        if (allocatedAmount > 0)
        {
            throw new ConflictException(
                "This charge already has payments applied and cannot be deleted. Post a credit adjustment instead.");
        }

        _dbContext.Charges.Remove(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>US-35: marks a charge Voided instead of deleting it -- it stays visible on the
    /// unit statement (badged "Voided" rather than disappearing) but stops counting toward the
    /// balance, for the case where a charge was correctly posted but should be forgiven/
    /// cancelled with a transparent record of that, as opposed to DeleteCharge's "this should
    /// never have existed" hard removal. Same lock rule as Update/Delete: once a payment has
    /// been allocated, voiding is blocked too -- forgiving a charge someone already paid
    /// against needs a ChargeAdjustment (a real credit), not a silent status flip that would
    /// leave their payment looking unaccounted for.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> VoidCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await FindChargeAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        if (charge.Status == ChargeLifecycleStatus.Voided)
        {
            throw new ConflictException("This charge has already been voided.");
        }

        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        if (allocatedAmount > 0)
        {
            throw new ConflictException(
                "This charge already has payments applied and cannot be voided. Post a credit adjustment instead.");
        }

        charge.Status = ChargeLifecycleStatus.Voided;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(BuildChargeResponse(charge, 0m, []));
    }

    /// <summary>US-35: the audit-compliant correction path -- a signed line-item adjustment
    /// (credit lowers what's owed, debit raises it) with a mandatory Reason, appended to the
    /// charge's history rather than editing its Amount. Deliberately NOT gated by the lock
    /// check that guards Update/Delete/Void: this endpoint is what those three redirect to
    /// once a charge is locked, and it's equally valid on an unlocked charge (e.g. a goodwill
    /// credit that should leave the original posted Amount visible on the record).</summary>
    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateChargeAdjustment(
        Guid propertyId, Guid id, [FromBody] CreateChargeAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var charge = await _authorizationService.EnsureSameTenantAsync(
            User, await FindChargeAsync(propertyId, id, cancellationToken),
            $"Charge '{id}' was not found on this property.", cancellationToken);

        var reason = _sanitizer.Sanitize(request.Reason)!;
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[nameof(request.Reason)] = ["Reason is required."];
        }
        else if (reason.Length > 500)
        {
            errors[nameof(request.Reason)] = ["Reason must be 500 characters or fewer."];
        }

        if (request.Amount <= 0)
        {
            errors[nameof(request.Amount)] = ["Amount must be greater than zero."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var adjustment = new ChargeAdjustment
        {
            Id = Guid.NewGuid(),
            TargetChargeId = charge.Id,
            AdjustmentType = request.AdjustmentType,
            Amount = request.Amount,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.ChargeAdjustments.Add(adjustment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audit Refinement Sprint: was StatusCode(201, ...) -- no Location header, unlike
        // every other create endpoint in this codebase. Adjustments have no standalone
        // GET-by-id of their own (they're only ever read nested under their parent charge,
        // via GetCharge/GetStatement), so this points at the charge they now belong to.
        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = charge.Id }, ToAdjustmentResponse(adjustment));
    }

    private async Task<Charge?> FindChargeAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Charges.FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Id == id, cancellationToken);

    /// <summary>Single-charge convenience wrapper over the batch method below -- still just
    /// 3 queries for one charge, same cost as before, kept for the lock-check call sites
    /// (Update/Delete/Void/CreateChargeAdjustment) that only ever need one charge at a time.</summary>
    private async Task<decimal> GetAllocatedAmountAsync(Guid chargeId, CancellationToken cancellationToken) =>
        (await GetAllocatedAmountsByChargeAsync([chargeId], cancellationToken)).GetValueOrDefault(chargeId, 0m);

    /// <summary>Sums PaymentAllocation (applied at waterfall time, when a payment was
    /// logged), CreditAllocation (US-37: applied later, when a PM ran "Apply Credits to
    /// Charges" against previously-unallocated overpayment credit), and
    /// DepositSettlementAllocation (US-39: applied via "Settle Deposit") -- all three lock a
    /// charge and count toward its PaymentStatus/OutstandingAmount identically. Batched
    /// across every requested charge ID in 3 queries total, however many charges are asked
    /// for -- the fix for GetCharges' N+1 (audit finding).</summary>
    private async Task<Dictionary<Guid, decimal>> GetAllocatedAmountsByChargeAsync(
        List<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var fromPayments = await _dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId))
            .GroupBy(a => a.ChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AllocatedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);
        var fromCredits = await _dbContext.CreditAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .GroupBy(a => a.TargetChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AppliedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);
        var fromDeposits = await _dbContext.DepositSettlementAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .GroupBy(a => a.TargetChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AppliedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);

        return chargeIds.ToDictionary(
            id => id,
            id => fromPayments.GetValueOrDefault(id, 0m) + fromCredits.GetValueOrDefault(id, 0m) + fromDeposits.GetValueOrDefault(id, 0m));
    }

    private async Task<ChargeResponse> BuildChargeResponseAsync(Charge charge, CancellationToken cancellationToken)
    {
        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        var adjustments = await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => a.TargetChargeId == charge.Id)
            .ToListAsync(cancellationToken);

        return BuildChargeResponse(charge, allocatedAmount, adjustments);
    }

    private static ChargeResponse BuildChargeResponse(Charge charge, decimal allocatedAmount, List<ChargeAdjustment> adjustments)
    {
        var netAdjustment = ChargeLedgerMath.NetAdjustment(adjustments);
        var outstandingAmount = charge.Status == ChargeLifecycleStatus.Voided
            ? 0m
            : ChargeLedgerMath.Outstanding(charge.Amount, netAdjustment, allocatedAmount);

        var paymentStatus = allocatedAmount <= 0
            ? ChargePaymentStatus.Unpaid
            : outstandingAmount <= 0
                ? ChargePaymentStatus.Paid
                : ChargePaymentStatus.Partial;

        return new ChargeResponse(
            charge.Id,
            charge.PropertyId,
            charge.Description,
            charge.Amount,
            charge.DueDate,
            charge.AccountingCode,
            charge.Category,
            charge.Status,
            allocatedAmount,
            outstandingAmount,
            paymentStatus,
            IsLocked: allocatedAmount > 0,
            charge.Notes);
    }

    private static ChargeAdjustmentResponse ToAdjustmentResponse(ChargeAdjustment adjustment) => new(
        adjustment.Id, adjustment.AdjustmentType, adjustment.Amount, adjustment.Reason, adjustment.CreatedAt);

    private sealed record SanitizedFields(string Description, string? AccountingCode, string? Notes);

    private SanitizedFields ValidateAndSanitize(UpsertChargeRequest request)
    {
        var description = _sanitizer.Sanitize(request.Description)!;
        var accountingCode = NullIfBlank(_sanitizer.Sanitize(request.AccountingCode));
        var notes = NullIfBlank(_sanitizer.Sanitize(request.Notes));

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(description))
        {
            errors[nameof(request.Description)] = ["Description is required."];
        }
        else if (description.Length > 200)
        {
            errors[nameof(request.Description)] = ["Description must be 200 characters or fewer."];
        }

        if (request.Amount <= 0)
        {
            errors[nameof(request.Amount)] = ["Amount must be greater than zero."];
        }

        if (accountingCode is { Length: > 50 })
        {
            errors[nameof(request.AccountingCode)] = ["Accounting code must be 50 characters or fewer."];
        }

        if (notes is { Length: > 500 })
        {
            errors[nameof(request.Notes)] = ["Notes must be 500 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SanitizedFields(description, accountingCode, notes);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
