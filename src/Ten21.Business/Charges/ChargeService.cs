using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Charges;

/// <summary>
/// Business-layer refactor: the charge CRUD/lock-rule logic extracted from
/// ChargesController. Methods taking a `Charge charge` parameter directly (rather than
/// propertyId+id) expect the caller to have already resolved AND resource-authorized it --
/// that BOLA/IDOR check (IAuthorizationService.EnsureSameTenantAsync) stays in the
/// controller, an ASP.NET Core-specific concern deliberately not pulled into this layer.
/// FindAsync exists so the controller has something to resolve+authorize in the first place.
///
/// Owns the unit-of-work: holds Ten21DbContext directly for every trivial single-table
/// read/stage (find/add/remove) and calls SaveChangesAsync exactly once per operation, after
/// all of that operation's changes are staged -- see CLAUDE.md's data-access rules.
/// ChargeRepository is only for the two genuinely batched/grouped queries; it never touches
/// SaveChangesAsync.
///
/// No interface -- nothing else implements or mocks this; it's registered concrete in DI
/// (see DependencyInjection.cs) and injected concrete into ChargesController.
///
/// GetStatement/GetStatementPdf (which also touch Payments/Credits/Deposits/Refunds) moved to
/// StatementService instead -- a separate cross-cutting concern, not bundled into this one.
/// </summary>
public class ChargeService
{
    private readonly Ten21DbContext _dbContext;
    private readonly ChargeRepository _repository;
    private readonly IInputSanitizer _sanitizer;

    public ChargeService(Ten21DbContext dbContext, ChargeRepository repository, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sanitizer = sanitizer;
    }

    public async Task<IReadOnlyList<ChargeResponse>> GetChargesAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _dbContext.Charges.AsNoTracking()
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var allocatedAmountsByChargeId = await _repository.GetAllocatedAmountsAsync(chargeIds, cancellationToken);
        var adjustmentsByChargeId = await _repository.ListAdjustmentsByChargeIdsAsync(chargeIds, cancellationToken);

        return charges
            .Select(charge => BuildResponse(
                charge,
                allocatedAmountsByChargeId.GetValueOrDefault(charge.Id, 0m),
                adjustmentsByChargeId.GetValueOrDefault(charge.Id, [])))
            .ToList();
    }

    public Task<Charge?> FindAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.Charges.FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Id == id, cancellationToken);

    public async Task<ChargeResponse> BuildResponseAsync(Charge charge, CancellationToken cancellationToken)
    {
        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        var adjustments = await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => a.TargetChargeId == charge.Id)
            .ToListAsync(cancellationToken);
        return BuildResponse(charge, allocatedAmount, adjustments);
    }

    /// <summary>Synchronous overload for callers (e.g. StatementService) that already have
    /// the allocated amount and adjustments in hand from a batched query and just need the
    /// same response-shaping rule applied, without a second round-trip.</summary>
    public ChargeResponse BuildResponse(Charge charge, decimal allocatedAmount, IReadOnlyList<ChargeAdjustment> adjustments)
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

    public async Task<ChargeResponse> CreateAsync(Guid propertyId, UpsertChargeRequest request, CancellationToken cancellationToken)
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

        return BuildResponse(charge, 0m, []);
    }

    /// <summary>US-35: only unlocked (zero dollars allocated) charges can be edited directly
    /// -- once a payment has been applied, the base Amount is permanently locked and
    /// corrections must go through a ChargeAdjustment instead.</summary>
    public async Task<ChargeResponse> UpdateAsync(Charge charge, UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);
        await EnsureUnlockedAsync(
            charge,
            "This charge already has payments applied and its amount is locked. Post a credit or debit adjustment instead.",
            cancellationToken);

        charge.Description = fields.Description;
        charge.Amount = request.Amount;
        charge.DueDate = request.DueDate;
        charge.AccountingCode = fields.AccountingCode;
        charge.Category = request.Category;
        charge.Notes = fields.Notes;
        charge.AllocationPriority = Charge.DefaultAllocationPriorityFor(request.Category);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return BuildResponse(charge, 0m, []);
    }

    /// <summary>Always a soft delete (Charge is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- a posted charge is a financial record,
    /// never hard-erased. Same lock rule as UpdateAsync: only an unpaid charge can be removed
    /// outright.</summary>
    public async Task DeleteAsync(Charge charge, CancellationToken cancellationToken)
    {
        await EnsureUnlockedAsync(
            charge,
            "This charge already has payments applied and cannot be deleted. Post a credit adjustment instead.",
            cancellationToken);

        _dbContext.Charges.Remove(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>US-35: marks a charge Voided instead of deleting it -- it stays visible on the
    /// unit statement (badged "Voided" rather than disappearing) but stops counting toward the
    /// balance. Same lock rule as Update/Delete: once a payment has been allocated, voiding is
    /// blocked too -- forgiving a charge someone already paid against needs a ChargeAdjustment
    /// (a real credit), not a silent status flip that would leave their payment looking
    /// unaccounted for.</summary>
    public async Task<ChargeResponse> VoidAsync(Charge charge, CancellationToken cancellationToken)
    {
        if (charge.Status == ChargeLifecycleStatus.Voided)
        {
            throw new ConflictException("This charge has already been voided.");
        }

        await EnsureUnlockedAsync(
            charge,
            "This charge already has payments applied and cannot be voided. Post a credit adjustment instead.",
            cancellationToken);

        charge.Status = ChargeLifecycleStatus.Voided;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return BuildResponse(charge, 0m, []);
    }

    /// <summary>US-35: the audit-compliant correction path -- a signed line-item adjustment
    /// (credit lowers what's owed, debit raises it) with a mandatory Reason, appended to the
    /// charge's history rather than editing its Amount. Deliberately NOT gated by the lock
    /// check that guards Update/Delete/Void: this is what those three redirect callers to once
    /// a charge is locked, and it's equally valid on an unlocked charge (e.g. a goodwill credit
    /// that should leave the original posted Amount visible on the record).</summary>
    public async Task<ChargeAdjustmentResponse> CreateAdjustmentAsync(
        Charge charge, CreateChargeAdjustmentRequest request, CancellationToken cancellationToken)
    {
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

        return ToAdjustmentResponse(adjustment);
    }

    private async Task EnsureUnlockedAsync(Charge charge, string conflictMessage, CancellationToken cancellationToken)
    {
        var allocatedAmount = await GetAllocatedAmountAsync(charge.Id, cancellationToken);
        if (allocatedAmount > 0)
        {
            throw new ConflictException(conflictMessage);
        }
    }

    private async Task<decimal> GetAllocatedAmountAsync(Guid chargeId, CancellationToken cancellationToken) =>
        (await _repository.GetAllocatedAmountsAsync([chargeId], cancellationToken)).GetValueOrDefault(chargeId, 0m);

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
