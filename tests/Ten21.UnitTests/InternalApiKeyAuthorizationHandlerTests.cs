using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-45 (Sprint 9): real concrete IHttpContextAccessor/IConfiguration
/// implementations, not mocks -- matches this codebase's existing convention (no mocking
/// library) already used for TestAuthorizationService.</summary>
public class InternalApiKeyAuthorizationHandlerTests
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    private static InternalApiKeyAuthorizationHandler CreateHandler(string? configuredKey, string? providedKey)
    {
        var httpContext = new DefaultHttpContext();
        if (providedKey is not null)
        {
            httpContext.Request.Headers[InternalApiKeyAuthorizationHandler.ApiKeyHeaderName] = providedKey;
        }
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredKey is null
                ? []
                : new Dictionary<string, string?> { ["Internal:ApiKey"] = configuredKey })
            .Build();

        return new InternalApiKeyAuthorizationHandler(httpContextAccessor, configuration);
    }

    [Fact]
    public async Task Succeeds_WhenTheProvidedKeyMatchesTheConfiguredKey()
    {
        var handler = CreateHandler(configuredKey: "correct-secret", providedKey: "correct-secret");
        var requirement = new PermissionRequirement(Permissions.Billing.RunCycle);
        var context = new AuthorizationHandlerContext([requirement], AnonymousPrincipal, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenTheProvidedKeyIsWrong()
    {
        var handler = CreateHandler(configuredKey: "correct-secret", providedKey: "wrong-secret");
        var requirement = new PermissionRequirement(Permissions.Billing.RunCycle);
        var context = new AuthorizationHandlerContext([requirement], AnonymousPrincipal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenNoKeyIsProvidedAtAll()
    {
        var handler = CreateHandler(configuredKey: "correct-secret", providedKey: null);
        var requirement = new PermissionRequirement(Permissions.Billing.RunCycle);
        var context = new AuthorizationHandlerContext([requirement], AnonymousPrincipal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenNoKeyIsConfiguredAtAll()
    {
        // Fail-closed default: an unconfigured Internal:ApiKey means the internal-key path
        // is simply unavailable, not "anything goes."
        var handler = CreateHandler(configuredKey: null, providedKey: "anything");
        var requirement = new PermissionRequirement(Permissions.Billing.RunCycle);
        var context = new AuthorizationHandlerContext([requirement], AnonymousPrincipal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_ForADifferentPermission_EvenWithAValidKey()
    {
        // Scoped strictly to Permissions.Billing.RunCycle -- a valid key must never satisfy
        // an unrelated policy like Lease.Manage.
        var handler = CreateHandler(configuredKey: "correct-secret", providedKey: "correct-secret");
        var requirement = new PermissionRequirement(Permissions.Lease.Manage);
        var context = new AuthorizationHandlerContext([requirement], AnonymousPrincipal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
