using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// US-45 (Sprint 9): satisfies Permissions.Billing.RunCycle for an unattended caller with
/// no logged-in user -- the future owner/operator site's nightly scheduler, or a
/// developer-run curl/script -- authenticated via a shared secret header instead of a JWT.
/// Runs alongside PermissionClaimAuthorizationHandler for the SAME PermissionRequirement
/// type; either one succeeding satisfies the policy (ASP.NET Core dispatches every
/// registered handler for a requirement's type and succeeds if any of them calls
/// context.Succeed). Deliberately scoped to this ONE permission, not Permissions.Lease.Manage
/// or any other -- see the Billing permission's own doc comment for why a leaked key's blast
/// radius matters.
///
/// A normal user JWT is never rejected here -- this handler simply does nothing (never
/// calls Succeed OR Fail) when the header is absent or wrong, leaving
/// PermissionClaimAuthorizationHandler free to succeed on its own for a real PM's claim.
/// </summary>
public class InternalApiKeyAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string ApiKeyHeaderName = "X-Internal-Api-Key";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string? _configuredApiKey;

    public InternalApiKeyAuthorizationHandler(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuredApiKey = configuration["Internal:ApiKey"];
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.Permission != Permissions.Billing.RunCycle)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(_configuredApiKey))
        {
            return Task.CompletedTask;
        }

        var providedKey = _httpContextAccessor.HttpContext?.Request.Headers[ApiKeyHeaderName].ToString();
        if (!string.IsNullOrEmpty(providedKey) && string.Equals(providedKey, _configuredApiKey, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
