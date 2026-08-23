using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Ten21.Infrastructure.Identity.Services;
using Ten21.Infrastructure.Middleware;
using Xunit;

namespace Ten21.UnitTests;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://api.ten21.io.test",
                ["Jwt:Audience"] = "https://app.ten21.io.test",
                // Test-only key -- 32+ bytes, never used outside this test process.
                ["Jwt:Key"] = "unit-test-signing-key-do-not-use-in-real-envs!",
            })
            .Build();

        return new JwtTokenService(configuration);
    }

    [Fact]
    public void GenerateAccessToken_IncludesTenantUserAndRoleClaims()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = service.GenerateAccessToken(userId, tenantId, organizationId: null, roleName: "PropertyManager");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == "user_id").Value);
        Assert.Equal(tenantId.ToString(), jwt.Claims.Single(c => c.Type == TenantMiddleware.TenantIdClaimType).Value);
        Assert.Equal("PropertyManager", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void GenerateAccessToken_OmitsOrganizationClaim_WhenNull()
    {
        var service = CreateService();

        var token = service.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), organizationId: null, roleName: "Tenant");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == TenantMiddleware.OrganizationIdClaimType);
    }

    [Fact]
    public void GenerateAccessToken_IncludesOrganizationClaim_WhenProvided()
    {
        var service = CreateService();
        var organizationId = Guid.NewGuid();

        var token = service.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), organizationId, "PropertyManager");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        Assert.Equal(
            organizationId.ToString(),
            jwt.Claims.Single(c => c.Type == TenantMiddleware.OrganizationIdClaimType).Value);
    }

    [Fact]
    public void GenerateAccessToken_ExpiresApproximatelyFifteenMinutesFromNow()
    {
        var service = CreateService();
        var before = DateTimeOffset.UtcNow;

        var token = service.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), null, "Tenant");

        var expectedExpiry = before.AddMinutes(15);
        var difference = (token.ExpiresAtUtc - expectedExpiry).Duration();

        Assert.True(difference < TimeSpan.FromSeconds(5), $"Expected ~15 minutes, was off by {difference}");
    }

    [Fact]
    public void Constructor_ThrowsIfKeyMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://api.ten21.io.test",
                ["Jwt:Audience"] = "https://app.ten21.io.test",
                // Jwt:Key deliberately omitted
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));
    }
}
