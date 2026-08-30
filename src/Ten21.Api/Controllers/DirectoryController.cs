using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Directory;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-25: the community directory. See DirectoryService's own comment for the dual-consent
/// business rule; this controller only extracts/validates the caller's user_id claim (an
/// ASP.NET Core-specific concern) and delegates -- no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly DirectoryService _directoryService;

    public DirectoryController(DirectoryService directoryService)
    {
        _directoryService = directoryService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Directory.Read)]
    public async Task<IActionResult> GetDirectory(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (!Guid.TryParse(userIdClaim, out var callerId))
        {
            throw new UnauthorizedException("Missing or invalid caller identity.");
        }

        return Ok(await _directoryService.GetDirectoryAsync(callerId, cancellationToken));
    }

    [HttpGet("admin")]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetDirectoryAdmin(CancellationToken cancellationToken) =>
        Ok(await _directoryService.GetDirectoryAdminAsync(cancellationToken));
}
