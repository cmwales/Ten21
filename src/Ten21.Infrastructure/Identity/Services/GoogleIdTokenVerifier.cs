using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Identity.Services;

/// <summary>
/// US-15: verifies a Google ID token via Google.Apis.Auth's own signature/issuer/audience
/// checks -- never hand-rolled JWT parsing for a third party's tokens.
///
/// Deliberately does NOT throw at construction time when Google:ClientId is unconfigured,
/// unlike JwtTokenService's Jwt:Key check. Jwt:Key is load-bearing for every single
/// request; Google:ClientId is only touched by the one caller who actually hits
/// POST /api/auth/google. Throwing eagerly here would take down the ENTIRE API at startup
/// over a feature nobody's using yet -- exactly the "build it now, get credentials later"
/// tradeoff this story was built under. Every other auth flow (login, register, refresh)
/// keeps working with this unconfigured; only Google sign-in itself fails, cleanly, until
/// a real Client ID is set.
/// </summary>
public class GoogleIdTokenVerifier : IGoogleIdTokenVerifier
{
    private readonly string? _clientId;

    public GoogleIdTokenVerifier(IConfiguration configuration)
    {
        _clientId = configuration["Google:ClientId"];
    }

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_clientId],
            });

            return new GoogleIdentity(
                payload.Subject, payload.Email, payload.EmailVerified, payload.GivenName, payload.FamilyName);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
