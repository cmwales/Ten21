using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Middleware;

namespace Ten21.Infrastructure.Identity.Services;

/// <summary>
/// HMAC-signed JWT issuance. Reads Issuer/Audience/Key from configuration -- Key is a real
/// secret and comes from User Secrets locally (see README), never from appsettings.json,
/// which ships with an empty value on purpose so a misconfigured environment fails loudly.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    // 15 minutes per SECURITY.docx §2 ("stateless 15-minute JWT Access Tokens"). A constant,
    // not a config value -- this is a security policy decision, not an environment-specific
    // tuning knob, so it shouldn't be silently different between dev and prod.
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    // Deliberately much shorter than a normal access token: an interim token (US-15/US-17)
    // exists only to bridge a single next step (complete a profile, submit a 2FA code), not
    // to be a general-purpose session.
    private static readonly TimeSpan InterimAccessTokenLifetime = TimeSpan.FromMinutes(10);

    private readonly string _issuer;
    private readonly string _audience;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IConfiguration configuration)
    {
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set it via `dotnet user-secrets set \"Jwt:Key\" \"<value>\"` " +
                "in src/Ten21.Api -- see README. It must never be committed to appsettings.json.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken GenerateAccessToken(Guid userId, Guid tenantId, Guid? organizationId, string roleName)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("user_id", userId.ToString()),
            new(TenantMiddleware.TenantIdClaimType, tenantId.ToString()),
            new(ClaimTypes.Role, roleName),
        };

        if (organizationId is not null)
        {
            claims.Add(new Claim(TenantMiddleware.OrganizationIdClaimType, organizationId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: _signingCredentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(tokenValue, expiresAtUtc);
    }

    public AccessToken GenerateInterimAccessToken(Guid userId, string purpose)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(InterimAccessTokenLifetime);

        // No tenant_id, no organization_id, no role claim -- deliberately. See this
        // method's interface-level doc comment for why that's the entire point.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("user_id", userId.ToString()),
            new(TokenPurposes.ClaimType, purpose),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: _signingCredentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(tokenValue, expiresAtUtc);
    }
}
