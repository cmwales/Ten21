using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ten21.UnitTests;

/// <summary>
/// Audit Refinement Sprint: a bare, non-null ControllerBase.User for the controller tests
/// that now call IAuthorizationService.AuthorizeAsync (via ResourceAuthorizationExtensions) --
/// a freshly `new`'d controller's ControllerContext.HttpContext is null by default, which
/// would otherwise reach AuthorizeAsync as a null principal. Claims content doesn't matter to
/// SameTenantResourceAuthorizationHandler (it only inspects the resource and ITenantContext,
/// never context.User), so an empty, unauthenticated-by-default identity is enough.
/// </summary>
internal static class TestControllerContext
{
    public static ControllerContext Create() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")),
        },
    };
}
