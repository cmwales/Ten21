using System.Security.Cryptography;

namespace Ten21.Infrastructure.Identity.Services;

/// <summary>
/// Pure functions for refresh token generation/hashing, deliberately separated from
/// RefreshTokenService's DB-touching methods so they're unit-testable in isolation --
/// no DbContext, no mocking, just inputs and outputs.
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>
    /// Generates a cryptographically random 256-bit raw token, base64url-encoded (URL/cookie
    /// safe -- no padding, no +/ characters).
    /// </summary>
    public static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// SHA-256 hash of a raw token, hex-encoded. Deterministic (same input -> same output),
    /// which is what makes "look up by hash" possible -- this is NOT a slow/salted password
    /// hash on purpose, because refresh tokens are already 256 bits of real entropy, not a
    /// human-chosen secret an attacker could dictionary-attack.
    /// </summary>
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
