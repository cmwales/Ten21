using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Leases;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-30: attaches a ResidentProfile to a Property with contract dates, base rent, a
/// recurring billing anchor day, and optional recurring sub-charges -- the prerequisite data
/// this Sprint establishes for Sprint 7's automated recurring billing. Nested resource
/// (api/properties/{propertyId}/leases), same BOLA/IDOR-safe convention as ResidentsController:
/// every action re-checks PropertyId == the route's propertyId rather than trusting a bare
/// {id} lookup.
///
/// Business-layer refactor: all business logic AND all data access now live in LeaseService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/leases")]
public class LeasesController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly LeaseService _leaseService;

    public LeasesController(IAuthorizationService authorizationService, LeaseService leaseService)
    {
        _authorizationService = authorizationService;
        _leaseService = leaseService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Lease.Read)]
    public async Task<IActionResult> GetLeases(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _leaseService.GetLeasesAsync(propertyId, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Read)]
    public async Task<IActionResult> GetLease(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        return Ok(await _leaseService.BuildResponseAsync(propertyId, lease, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> CreateLease(
        Guid propertyId, [FromBody] UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        var response = await _leaseService.CreateAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetLease), new { propertyId, id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> UpdateLease(
        Guid propertyId, Guid id, [FromBody] UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        return Ok(await _leaseService.UpdateAsync(propertyId, lease, request, cancellationToken));
    }

    [HttpPost("{id:guid}/move-in-charge")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> CreateMoveInCharge(
        Guid propertyId, Guid id, [FromBody] CreateMoveInChargeRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        return Ok(await _leaseService.CreateMoveInChargeAsync(propertyId, lease, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> DeleteLease(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        await _leaseService.DeleteAsync(lease, cancellationToken);
        return NoContent();
    }

    /// <summary>US-45: null (204) when the lease has no policy attached yet, rather than
    /// 404 -- "no late fee policy configured" is a normal, expected state for a lease, not
    /// a missing resource.</summary>
    [HttpGet("{id:guid}/late-fee-policy")]
    [Authorize(Policy = Permissions.Lease.Read)]
    public async Task<IActionResult> GetLateFeePolicy(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        var policy = await _leaseService.GetLateFeePolicyAsync(id, cancellationToken);
        return policy is null ? NoContent() : Ok(policy);
    }

    [HttpPut("{id:guid}/late-fee-policy")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> UpsertLateFeePolicy(
        Guid propertyId, Guid id, [FromBody] LateFeePolicyRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        return Ok(await _leaseService.UpsertLateFeePolicyAsync(lease, request, cancellationToken));
    }

    [HttpDelete("{id:guid}/late-fee-policy")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> DeleteLateFeePolicy(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Lease '{id}' was not found on this property.";
        var lease = await _leaseService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, lease, notFoundMessage, cancellationToken);

        await _leaseService.DeleteLateFeePolicyAsync(id, cancellationToken);
        return NoContent();
    }
}
