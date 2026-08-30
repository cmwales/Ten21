using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Credits;
using Ten21.Api.Contracts.Deposits;
using Ten21.Application.Abstractions;
using Ten21.Application.Ledger;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-39: security deposit escrow -- collecting a deposit at move-in and settling it at
/// move-out. Kept as its own resource/controller (SecurityDeposit, not Charge or
/// PaymentTransaction) because deposit money is a liability held separately from operating
/// rental income, never rent actually received -- see SecurityDeposit's own class comment.
/// Same BOLA/IDOR-safe convention as every other ledger controller: nested under Property,
/// every action re-checks PropertyId == the route's propertyId.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/deposits")]
public class DepositsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;
    private readonly IAuthorizationService _authorizationService;

    public DepositsController(Ten21DbContext dbContext, IInputSanitizer sanitizer, IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetDeposits(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var deposits = await _dbContext.SecurityDeposits.AsNoTracking()
            .Where(d => d.PropertyId == propertyId)
            .OrderByDescending(d => d.CollectedDate)
            .ToListAsync(cancellationToken);

        var responses = new List<SecurityDepositResponse>(deposits.Count);
        foreach (var deposit in deposits)
        {
            responses.Add(await BuildResponseAsync(deposit, cancellationToken));
        }

        return Ok(responses);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetDeposit(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var deposit = await _authorizationService.EnsureSameTenantAsync(
            User, await FindDepositAsync(propertyId, id, cancellationToken),
            $"Security deposit '{id}' was not found on this property.", cancellationToken);

        return Ok(await BuildResponseAsync(deposit, cancellationToken));
    }

    /// <summary>Dual-Anchor Attribution: if ResidentProfileId isn't specified, auto-defaults
    /// to the Primary Resident on the unit's active lease (Lease.ResidentId) -- the lease with
    /// the latest StartDate that hasn't ended. Throws a ValidationException if there's no
    /// active lease to default from, rather than silently picking an arbitrary resident.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CollectDeposit(
        Guid propertyId, [FromBody] CollectDepositRequest request, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Amount)] = ["Amount must be greater than zero."],
            });
        }

        Guid residentProfileId;
        if (request.ResidentProfileId is { } explicitResidentId)
        {
            var explicitResident = await _dbContext.ResidentProfiles
                .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == explicitResidentId, cancellationToken)
                ?? throw new NotFoundException($"Resident '{explicitResidentId}' was not found on this property.");
            residentProfileId = explicitResident.Id;
        }
        else
        {
            var activeLease = await _dbContext.Leases
                .Where(l => l.PropertyId == propertyId && l.Status != LeaseStatus.Ended)
                .OrderByDescending(l => l.StartDate)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    [nameof(request.ResidentProfileId)] = ["No active lease on this unit to default a resident from -- select one explicitly."],
                });
            residentProfileId = activeLease.ResidentId;
        }

        var deposit = new SecurityDeposit
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentProfileId = residentProfileId,
            OriginalAmount = request.Amount,
            AmountHeld = request.Amount,
            CollectedDate = request.CollectedDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SecurityDeposits.Add(deposit);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetDeposit), new { propertyId, id = deposit.Id }, await BuildResponseAsync(deposit, cancellationToken));
    }

    /// <summary>
    /// The whole point of this story: applies the deposit's entire AmountHeld against the
    /// unit's outstanding Charges in the same statutory priority order as the payment
    /// waterfall, then disburses whatever's left to the resident via a RefundTransaction
    /// (Reason = DepositReturn). If dues exceed AmountHeld, the full deposit is applied and
    /// nothing is refunded -- the unsatisfied remainder simply stays on the unit's normal
    /// Balance/OutstandingAmount figures, surfaced via UnitStatementResponse.AccountStatus
    /// ("TerminatedWithBalance") once this deposit is Settled.
    /// </summary>
    [HttpPost("{id:guid}/settle")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> SettleDeposit(
        Guid propertyId, Guid id, [FromBody] SettleDepositRequest request, CancellationToken cancellationToken)
    {
        var deposit = await _authorizationService.EnsureSameTenantAsync(
            User, await FindDepositAsync(propertyId, id, cancellationToken),
            $"Security deposit '{id}' was not found on this property.", cancellationToken);

        if (deposit.Status == SecurityDepositStatus.Settled)
        {
            throw new ConflictException("This security deposit has already been settled.");
        }

        var referenceNumber = NullIfBlank(_sanitizer.Sanitize(request.ReferenceNumber));
        if (referenceNumber is { Length: > 100 })
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.ReferenceNumber)] = ["Reference number must be 100 characters or fewer."],
            });
        }

        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingPaymentAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId)).ToListAsync(cancellationToken);
        var existingCreditAllocations = await _dbContext.CreditAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);
        var existingDepositAllocations = await _dbContext.DepositSettlementAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);

        var orderedCharges = ChargeLedgerMath.OrderByStatutoryPriority(activeCharges);

        var newAllocations = new List<DepositSettlementAllocation>();
        var remaining = deposit.AmountHeld;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var charge in orderedCharges)
        {
            if (remaining <= 0)
            {
                break;
            }

            var alreadyAllocated = existingPaymentAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + existingCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + existingDepositAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + newAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var netAdjustment = ChargeLedgerMath.NetAdjustment(existingAdjustments.Where(a => a.TargetChargeId == charge.Id));
            var outstanding = ChargeLedgerMath.Outstanding(charge.Amount, netAdjustment, alreadyAllocated);

            if (outstanding <= 0)
            {
                continue;
            }

            var amountToApply = Math.Min(remaining, outstanding);
            newAllocations.Add(new DepositSettlementAllocation
            {
                Id = Guid.NewGuid(),
                SecurityDepositId = deposit.Id,
                TargetChargeId = charge.Id,
                AppliedAmount = amountToApply,
                AppliedDate = today,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            remaining -= amountToApply;
        }

        _dbContext.DepositSettlementAllocations.AddRange(newAllocations);

        var amountApplied = newAllocations.Sum(a => a.AppliedAmount);
        var amountRefunded = remaining;

        RefundTransaction? refund = null;
        if (amountRefunded > 0)
        {
            refund = new RefundTransaction
            {
                Id = Guid.NewGuid(),
                ResidentProfileId = deposit.ResidentProfileId,
                PropertyId = propertyId,
                Amount = amountRefunded,
                RefundDate = today,
                TenderType = request.TenderType,
                ReferenceNumber = referenceNumber,
                Reason = RefundReason.DepositReturn,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.RefundTransactions.Add(refund);
        }

        deposit.AmountHeld = 0m;
        deposit.Status = SecurityDepositStatus.Settled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = activeCharges.ToDictionary(c => c.Id, c => c.Description);
        var allocationResponses = newAllocations.Select(a => new DepositSettlementAllocationResponse(
            a.Id, a.SecurityDepositId, a.TargetChargeId,
            chargeDescriptionsById.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        var residentName = await _dbContext.GetResidentNameAsync(deposit.ResidentProfileId, cancellationToken);
        var refundResponse = refund is null
            ? null
            : new RefundTransactionResponse(
                refund.Id, refund.ResidentProfileId, residentName, refund.PropertyId, refund.Amount,
                refund.RefundDate, refund.TenderType, refund.ReferenceNumber, refund.Reason, refund.CreatedAt);

        return Ok(new SettleDepositResponse(
            new SecurityDepositResponse(deposit.Id, deposit.PropertyId, deposit.ResidentProfileId, residentName,
                deposit.OriginalAmount, deposit.AmountHeld, deposit.CollectedDate, deposit.Status),
            amountApplied, amountRefunded, allocationResponses, refundResponse));
    }

    private async Task<SecurityDeposit?> FindDepositAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.SecurityDeposits.FirstOrDefaultAsync(d => d.PropertyId == propertyId && d.Id == id, cancellationToken);

    private async Task<SecurityDepositResponse> BuildResponseAsync(SecurityDeposit deposit, CancellationToken cancellationToken)
    {
        var residentName = await _dbContext.GetResidentNameAsync(deposit.ResidentProfileId, cancellationToken);
        return new SecurityDepositResponse(
            deposit.Id, deposit.PropertyId, deposit.ResidentProfileId, residentName,
            deposit.OriginalAmount, deposit.AmountHeld, deposit.CollectedDate, deposit.Status);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
