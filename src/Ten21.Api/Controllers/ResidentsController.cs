using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Residents;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-23/US-24: Tenant Profile Directory + Zero-Token Welcome & Provisioning. A
/// ResidentProfile always belongs to exactly one Property, addressed here as a nested
/// resource (api/properties/{propertyId}/residents) -- every action re-checks `PropertyId ==
/// propertyId` in its own query rather than trusting a bare {id} lookup, per CLAUDE.md's
/// BOLA/IDOR resource-based-authorization mandate.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// ResidentService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all. It only resolves+authorizes the resource and delegates.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/residents")]
public class ResidentsController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ResidentService _residentService;

    public ResidentsController(IAuthorizationService authorizationService, ResidentService residentService)
    {
        _authorizationService = authorizationService;
        _residentService = residentService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResidents(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _residentService.GetResidentsAsync(propertyId, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await _authorizationService.EnsureSameTenantAsync(
            User, await _residentService.FindAsync(propertyId, id, cancellationToken),
            $"Resident '{id}' was not found on this property.", cancellationToken);

        return Ok(ResidentService.BuildResponse(resident));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> CreateResident(
        Guid propertyId, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        var response = await _residentService.CreateAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetResident), new { propertyId, id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> UpdateResident(
        Guid propertyId, Guid id, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        var resident = await _authorizationService.EnsureSameTenantAsync(
            User, await _residentService.FindAsync(propertyId, id, cancellationToken),
            $"Resident '{id}' was not found on this property.", cancellationToken);

        return Ok(await _residentService.UpdateAsync(resident, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> DeleteResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await _authorizationService.EnsureSameTenantAsync(
            User, await _residentService.FindAsync(propertyId, id, cancellationToken),
            $"Resident '{id}' was not found on this property.", cancellationToken);

        await _residentService.DeleteAsync(resident, cancellationToken);
        return NoContent();
    }
}
