using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.ManualCharges;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-31: one-time, non-recurring charges/fines posted directly to a unit's (or a specific
/// resident's) ledger -- e.g. "Trash Violation Fine". Nested under a Property, same
/// BOLA/IDOR-safe convention as LeasesController/ResidentsController: every action re-checks
/// PropertyId == the route's propertyId rather than trusting a bare {id} lookup.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/manual-charges")]
public class ManualChargesController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public ManualChargesController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetManualCharges(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var charges = await _dbContext.ManualCharges
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);

        return Ok(charges.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetManualCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await FindManualChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

        return Ok(ToResponse(charge));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateManualCharge(
        Guid propertyId, [FromBody] UpsertManualChargeRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
        if (request.ResidentId is { } residentId)
        {
            await EnsureResidentBelongsToPropertyAsync(propertyId, residentId, cancellationToken);
        }
        var fields = ValidateAndSanitize(request);

        var charge = new ManualCharge
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentId = request.ResidentId,
            Description = fields.Description,
            Amount = request.Amount,
            DueDate = request.DueDate,
            AccountingCode = fields.AccountingCode,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.ManualCharges.Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetManualCharge), new { propertyId, id = charge.Id }, ToResponse(charge));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> UpdateManualCharge(
        Guid propertyId, Guid id, [FromBody] UpsertManualChargeRequest request, CancellationToken cancellationToken)
    {
        if (request.ResidentId is { } residentId)
        {
            await EnsureResidentBelongsToPropertyAsync(propertyId, residentId, cancellationToken);
        }
        var fields = ValidateAndSanitize(request);

        var charge = await FindManualChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

        charge.ResidentId = request.ResidentId;
        charge.Description = fields.Description;
        charge.Amount = request.Amount;
        charge.DueDate = request.DueDate;
        charge.AccountingCode = fields.AccountingCode;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(charge));
    }

    /// <summary>Always a soft delete (ManualCharge is ISoftDelete, and
    /// AuditSaveChangesInterceptor converts the Remove() below automatically) -- a posted
    /// charge/fine is a financial record, never hard-erased.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> DeleteManualCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var charge = await FindManualChargeAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Charge '{id}' was not found on this property.");

        _dbContext.ManualCharges.Remove(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    private async Task EnsureResidentBelongsToPropertyAsync(
        Guid propertyId, Guid residentId, CancellationToken cancellationToken)
    {
        var belongs = await _dbContext.ResidentProfiles
            .AnyAsync(r => r.Id == residentId && r.PropertyId == propertyId, cancellationToken);
        if (!belongs)
        {
            throw new NotFoundException($"Resident '{residentId}' was not found on this property.");
        }
    }

    private async Task<ManualCharge?> FindManualChargeAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.ManualCharges
            .FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Id == id, cancellationToken);

    private sealed record SanitizedFields(string Description, string? AccountingCode);

    private SanitizedFields ValidateAndSanitize(UpsertManualChargeRequest request)
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

    private static ManualChargeResponse ToResponse(ManualCharge charge) => new(
        charge.Id, charge.PropertyId, charge.ResidentId, charge.Description, charge.Amount, charge.DueDate, charge.AccountingCode);
}
