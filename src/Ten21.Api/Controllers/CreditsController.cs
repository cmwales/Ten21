using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Contracts.Credits;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-37: "Apply Credits to Charges" -- a manual, PM-triggered draw-down of a unit's retained
/// overpayment credit (PaymentTransaction.UnallocatedAmount) against its outstanding Charges.
/// Deliberately a button, not a background job: there's no recurring-billing engine in this
/// codebase yet to hang a scheduled "anchor date" drawdown off of, and building one just for
/// this would be a large, unrelated undertaking -- the PM explicitly asked for a manual
/// trigger instead.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/credits")]
public class CreditsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;

    public CreditsController(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Loads every payment on this unit with UnallocatedAmount > 0 (oldest first) and every
    /// outstanding Active charge (same statutory priority order as the payment waterfall --
    /// see PaymentsController.BuildWaterfallAllocationsAsync), then walks the charges in order,
    /// drawing from the oldest available credit source(s) until either that charge is
    /// satisfied or all credit is exhausted. Credit is unit-scoped, not resident-scoped, same
    /// as everything else charges touch -- a charge has no "which resident" of its own to
    /// restrict which resident's credit can pay it (see Charge's own class comment), so any
    /// resident's retained credit on this unit can satisfy any of the unit's outstanding
    /// charges. CreditAllocation.SourcePaymentTransactionId still records exactly which
    /// payment (and therefore which resident) supplied each dollar.
    /// </summary>
    [HttpPost("apply")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ApplyCreditsToCharges(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var paymentsWithCredit = await _dbContext.PaymentTransactions
            .Where(p => p.PropertyId == propertyId && p.UnallocatedAmount > 0)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        if (paymentsWithCredit.Count == 0)
        {
            return Ok(new ApplyCreditsResponse(0m, []));
        }

        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingPaymentAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        var existingCreditAllocations = await _dbContext.CreditAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

        var orderedCharges = activeCharges
            .OrderBy(c => c.AllocationPriority)
            .ThenBy(c => c.DueDate)
            .ToList();

        var newAllocations = new List<CreditAllocation>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var charge in orderedCharges)
        {
            if (paymentsWithCredit.All(p => p.UnallocatedAmount <= 0))
            {
                break;
            }

            var alreadyAllocated = existingPaymentAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + existingCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + newAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var netAdjustment = existingAdjustments.Where(a => a.TargetChargeId == charge.Id)
                .Sum(a => a.AdjustmentType == AdjustmentType.DebitAdjustment ? a.Amount : -a.Amount);
            var outstanding = Math.Max(0m, charge.Amount + netAdjustment - alreadyAllocated);

            while (outstanding > 0)
            {
                var sourcePayment = paymentsWithCredit.FirstOrDefault(p => p.UnallocatedAmount > 0);
                if (sourcePayment is null)
                {
                    break;
                }

                var amountToApply = Math.Min(outstanding, sourcePayment.UnallocatedAmount);
                newAllocations.Add(new CreditAllocation
                {
                    Id = Guid.NewGuid(),
                    SourcePaymentTransactionId = sourcePayment.Id,
                    TargetChargeId = charge.Id,
                    AppliedAmount = amountToApply,
                    AppliedDate = today,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                sourcePayment.UnallocatedAmount -= amountToApply;
                outstanding -= amountToApply;
            }
        }

        _dbContext.CreditAllocations.AddRange(newAllocations);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = activeCharges.ToDictionary(c => c.Id, c => c.Description);
        var responses = newAllocations.Select(a => new CreditAllocationResponse(
            a.Id, a.SourcePaymentTransactionId, a.TargetChargeId,
            chargeDescriptionsById.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        return Ok(new ApplyCreditsResponse(responses.Sum(r => r.AppliedAmount), responses));
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }
}
