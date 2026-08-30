using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Leases;
using Ten21.Domain.Common;
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
        var lease = await _authorizationService.EnsureSameTenantAsync(
            User, await _leaseService.FindAsync(propertyId, id, cancellationToken),
            $"Lease '{id}' was not found on this property.", cancellationToken);

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
        var lease = await _authorizationService.EnsureSameTenantAsync(
            User, await _leaseService.FindAsync(propertyId, id, cancellationToken),
            $"Lease '{id}' was not found on this property.", cancellationToken);

        return Ok(await _leaseService.UpdateAsync(propertyId, lease, request, cancellationToken));
    }

    [HttpPost("{id:guid}/move-in-charge")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> CreateMoveInCharge(
        Guid propertyId, Guid id, [FromBody] CreateMoveInChargeRequest request, CancellationToken cancellationToken)
    {
        var lease = await _authorizationService.EnsureSameTenantAsync(
            User, await _leaseService.FindAsync(propertyId, id, cancellationToken),
            $"Lease '{id}' was not found on this property.", cancellationToken);

        return Ok(await _leaseService.CreateMoveInChargeAsync(propertyId, lease, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> DeleteLease(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var lease = await _authorizationService.EnsureSameTenantAsync(
            User, await _leaseService.FindAsync(propertyId, id, cancellationToken),
            $"Lease '{id}' was not found on this property.", cancellationToken);

        await _leaseService.DeleteAsync(lease, cancellationToken);
        return NoContent();
    }
}
