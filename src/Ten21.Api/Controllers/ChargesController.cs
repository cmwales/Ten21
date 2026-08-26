using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Contracts.Credits;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
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

    public ChargesController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharges(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);

        var responses = new List<ChargeResponse>(charges.Count);
        foreach (var charge in charges)
        {
            responses.Add(await BuildChargeResponseAsync(charge, cancellationToken));
        }

        return Ok(responses);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await FindChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

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
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var allAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        // Ordered client-side, not via OrderBy() in the query -- the SQLite provider (used
        // only by this codebase's in-memory unit tests) can't translate ORDER BY over a
        // DateTimeOffset column; Postgres has no such issue, but ordering after ToListAsync
        // works identically on both and this list is always small (one property's charges).
        var allAdjustments = (await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .OrderBy(a => a.CreatedAt)
            .ToList();
        var allCreditAllocations = (await _dbContext.CreditAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(a => a.AppliedDate)
            .ToList();

        var payments = await _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .Where(p => p.PropertyId == propertyId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var chargeDescriptionsByIdForCredits = charges.ToDictionary(c => c.Id, c => c.Description);
        var creditResponses = allCreditAllocations.Select(a => new CreditAllocationResponse(
            a.Id, a.SourcePaymentTransactionId, a.TargetChargeId,
            chargeDescriptionsByIdForCredits.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        var chargeStatementItems = charges.Select(charge =>
        {
            var allocatedAmount = allAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + allCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var adjustmentsForCharge = allAdjustments.Where(a => a.TargetChargeId == charge.Id).ToList();
            var chargeResponse = BuildChargeResponse(charge, allocatedAmount, adjustmentsForCharge);
            return new ChargeStatementItemResponse(chargeResponse, adjustmentsForCharge.Select(ToAdjustmentResponse).ToList());
        }).ToList();

        var chargeDescriptionsById = charges.ToDictionary(c => c.Id, c => c.Description);
        var residentIds = payments.Select(p => p.ResidentProfileId).Distinct().ToList();
        var residentNamesById = await _dbContext.ResidentProfiles
            .Where(r => residentIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => $"{r.FirstName} {r.LastName}", cancellationToken);
        var paymentResponses = payments.Select(p => new PaymentTransactionResponse(
            p.Id, p.PropertyId, p.ResidentProfileId, residentNamesById.GetValueOrDefault(p.ResidentProfileId, "(unknown resident)"),
            p.PaymentDate, p.AmountPaid, p.TenderType, p.ReferenceNumber, p.Notes, p.UnallocatedAmount,
            p.Allocations.Select(a => new PaymentAllocationSummaryResponse(
                a.ChargeId,
                chargeDescriptionsById.GetValueOrDefault(a.ChargeId, "(unknown charge)"),
                a.AllocatedAmount)).ToList())).ToList();

        var refunds = await _dbContext.RefundTransactions
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.RefundDate)
            .ToListAsync(cancellationToken);
        var refundResidentIds = refunds.Select(r => r.ResidentProfileId).Distinct().ToList();
        var refundResidentNamesById = await _dbContext.ResidentProfiles
            .Where(r => refundResidentIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => $"{r.FirstName} {r.LastName}", cancellationToken);
        var refundResponses = refunds.Select(r => new RefundTransactionResponse(
            r.Id, r.ResidentProfileId, refundResidentNamesById.GetValueOrDefault(r.ResidentProfileId, "(unknown resident)"),
            r.PropertyId, r.Amount, r.RefundDate, r.TenderType, r.ReferenceNumber, r.Reason, r.CreatedAt)).ToList();

        // See UnitStatementResponse's own comment for why Payments uses total AmountPaid
        // (not just allocated amounts) -- an overpayment should show as a credit (negative
        // balance), not just floor debt at zero. Refunds are added BACK (US-37): once a
        // resident's held credit is actually paid out, the unit no longer owes it.
        var sumActiveCharges = charges.Where(c => c.Status == ChargeLifecycleStatus.Active).Sum(c => c.Amount);
        var sumDebits = allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.DebitAdjustment).Sum(a => a.Amount);
        var sumCredits = allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.CreditAdjustment).Sum(a => a.Amount);
        var sumPayments = payments.Sum(p => p.AmountPaid);
        var sumRefunds = refunds.Sum(r => r.Amount);
        var balance = sumActiveCharges + sumDebits - sumPayments - sumCredits + sumRefunds;

        // US-37: AvailableCredit is distinct from Balance -- Balance already reflects an
        // overpayment as a credit implicitly (via subtracting the full AmountPaid above), but
        // AvailableCredit is the more specific "how much of that is still sitting un-drawn-down
        // right now" figure that the "Apply Credits to Charges" / "Refund Credit Balance"
        // actions actually operate against. Applying credit to a charge moves money from here
        // into a charge's AllocatedAmount; refunding it moves money out the door (reflected in
        // Balance above instead) -- neither changes Balance directly.
        var availableCredit = payments.Sum(p => p.UnallocatedAmount);

        return Ok(new UnitStatementResponse(
            propertyId, balance, availableCredit, chargeStatementItems, paymentResponses, creditResponses, refundResponses));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateCharge(
        Guid propertyId, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
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

        var charge = await FindChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

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
        var charge = await FindChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

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
        var charge = await FindChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

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
        var charge = await FindChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

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

        return StatusCode(StatusCodes.Status201Created, ToAdjustmentResponse(adjustment));
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    private async Task<Charge?> FindChargeAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Charges.FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Id == id, cancellationToken);

    /// <summary>Sums BOTH PaymentAllocation (applied at waterfall time, when a payment was
    /// logged) and CreditAllocation (US-37: applied later, when a PM ran "Apply Credits to
    /// Charges" against previously-unallocated overpayment credit) -- either one locks a
    /// charge and counts toward its PaymentStatus/OutstandingAmount identically.</summary>
    private async Task<decimal> GetAllocatedAmountAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        var fromPayments = await _dbContext.PaymentAllocations
            .Where(a => a.ChargeId == chargeId)
            .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;
        var fromCredits = await _dbContext.CreditAllocations
            .Where(a => a.TargetChargeId == chargeId)
            .SumAsync(a => (decimal?)a.AppliedAmount, cancellationToken) ?? 0m;
        return fromPayments + fromCredits;
    }

    private async Task<ChargeResponse> BuildChargeResponseAsync(Charge charge, CancellationToken cancellationToken)
    {
        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        var adjustments = await _dbContext.ChargeAdjustments
            .Where(a => a.TargetChargeId == charge.Id)
            .ToListAsync(cancellationToken);

        return BuildChargeResponse(charge, allocatedAmount, adjustments);
    }

    private static ChargeResponse BuildChargeResponse(Charge charge, decimal allocatedAmount, List<ChargeAdjustment> adjustments)
    {
        var netAdjustment = adjustments.Sum(a => a.AdjustmentType == AdjustmentType.DebitAdjustment ? a.Amount : -a.Amount);
        var netAmount = charge.Amount + netAdjustment;
        var outstandingAmount = charge.Status == ChargeLifecycleStatus.Voided
            ? 0m
            : Math.Max(0m, netAmount - allocatedAmount);

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
            IsLocked: allocatedAmount > 0);
    }

    private static ChargeAdjustmentResponse ToAdjustmentResponse(ChargeAdjustment adjustment) => new(
        adjustment.Id, adjustment.AdjustmentType, adjustment.Amount, adjustment.Reason, adjustment.CreatedAt);

    private sealed record SanitizedFields(string Description, string? AccountingCode);

    private SanitizedFields ValidateAndSanitize(UpsertChargeRequest request)
    {
        var description = _sanitizer.Sanitize(request.Description)!;
        var accountingCode = NullIfBlank(_sanitizer.Sanitize(request.AccountingCode));

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

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SanitizedFields(description, accountingCode);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
