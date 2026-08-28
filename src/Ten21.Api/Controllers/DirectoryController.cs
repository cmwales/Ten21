using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Directory;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-25: the community directory -- a Tenant-role resident sees fellow residents who
/// opted in (ResidentProfile.ShowInDirectory) at a property whose PM also opted in
/// (Property.AllowTenantDirectory), scoped to properties sharing the caller's own street
/// address ("neighboring units" in the flat Property model, where a suite is its own
/// Property row rather than a child of a shared parent).
///
/// Deliberately NOT parameterized by propertyId -- the caller's own occupancy (resolved
/// from their user_id claim via ResidentProfile.UserId) is what scopes this query, so
/// there is no client-suppliable property/tenant identifier to tamper with for BOLA
/// purposes; a Tenant simply cannot ask "show me the directory for property X."
/// </summary>
[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;

    public DirectoryController(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Directory.Read)]
    public async Task<IActionResult> GetDirectory(CancellationToken cancellationToken)
    {
        // Directive 4 (Refinement Sprint): a PM can turn the whole community directory off
        // workspace-wide via /admin/settings, independent of any individual resident's own
        // ShowInDirectory opt-in. No settings row yet (lazy-created by
        // WorkspaceSettingsController) means the default -- enabled -- applies.
        var directoryEnabled = await _dbContext.WorkspaceSettings
            .Select(s => (bool?)s.EnableCommunityDirectory)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!directoryEnabled)
        {
            throw new ForbiddenException("The community directory is disabled for this workspace.");
        }

        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (!Guid.TryParse(userIdClaim, out var callerId))
        {
            throw new UnauthorizedException("Missing or invalid caller identity.");
        }

        var occupiedPropertyIds = await _dbContext.ResidentProfiles
            .Where(r => r.UserId == callerId)
            .Select(r => r.PropertyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (occupiedPropertyIds.Count == 0)
        {
            return Ok(Array.Empty<DirectoryEntryResponse>());
        }

        // One query per occupied property (in practice almost always exactly one) rather
        // than a single composite-tuple .Contains(), which doesn't translate reliably
        // across EF Core providers for anonymous-type equality.
        var siblingPropertyIds = new HashSet<Guid>();
        foreach (var occupiedPropertyId in occupiedPropertyIds)
        {
            var occupied = await _dbContext.Properties.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == occupiedPropertyId, cancellationToken);
            if (occupied is null)
            {
                continue;
            }

            var siblingIds = await _dbContext.Properties
                .Where(p =>
                    p.AllowTenantDirectory
                    && p.StreetAddress1 == occupied.StreetAddress1
                    && p.City == occupied.City
                    && p.State == occupied.State
                    && p.PostalCode == occupied.PostalCode
                    && p.Country == occupied.Country)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in siblingIds)
            {
                siblingPropertyIds.Add(id);
            }
        }

        var entries = await _dbContext.ResidentProfiles
            .Where(r => siblingPropertyIds.Contains(r.PropertyId) && r.ShowInDirectory && r.UserId != callerId)
            .Join(
                _dbContext.Properties,
                resident => resident.PropertyId,
                property => property.Id,
                (resident, property) => new DirectoryEntryResponse(resident.FirstName, resident.LastName, property.UnitIdentifier))
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }
}
