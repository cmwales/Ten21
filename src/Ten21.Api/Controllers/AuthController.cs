using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ten21.Api.Auth;
using Ten21.Application.Abstractions;
using Ten21.Business.Auth;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Middleware;
using Ten21.Infrastructure.RateLimiting;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-02: Identity & Refresh Token Pipeline.
///
/// Business-layer refactor: all business logic AND all data access now live in AuthService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all. It only
/// handles the concerns that genuinely require the live HTTP request: reading claims off
/// User, reading/writing the refresh-token cookie (HTTP-only, never in a JSON body), and
/// resolving the caller's IP -- everything else is a plain value passed into AuthService.
///
/// [EnableRateLimiting] applies the 5-req/min-per-IP sliding window (US-05) to every action
/// in this controller, including /me -- SECURITY.docx's wording covers all of /api/auth/*
/// without carving out exceptions, and rate-limiting token refresh too is reasonable
/// defense against refresh-token-guessing attempts as well as login brute-forcing.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(AuthRateLimiterPolicy.PolicyName)]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IWebHostEnvironment _environment;

    public AuthController(AuthService authService, IWebHostEnvironment environment)
    {
        _authService = authService;
        _environment = environment;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, GetClientIp(), cancellationToken);
        RefreshTokenCookie.Set(Response, result.RawRefreshToken, _environment);
        return Ok(result.Response);
    }

    [HttpPost("resend-activation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendActivation(
        [FromBody] ResendActivationRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendActivationAsync(request.Email, cancellationToken);
        return Ok(new GenericAcknowledgementResponse(
            "If that email exists and isn't already verified, we've sent a new confirmation link."));
    }

    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateAccountRequest request, CancellationToken cancellationToken)
    {
        await _authService.ActivateAsync(request.UserId, request.Token, cancellationToken);
        return Ok(new GenericAcknowledgementResponse("Your email has been verified."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request.Email, cancellationToken);
        return Ok(new GenericAcknowledgementResponse(
            "If that email exists, we've sent password reset instructions."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(
            request.UserId, request.Token, request.NewPassword, GetClientIp(), cancellationToken);
        return Ok(new GenericAcknowledgementResponse("Your password has been reset. You can now log in."));
    }

    /// <summary>
    /// US-15: Google Sign-In. A user with no workspace yet gets an interim token and must
    /// call POST /api/auth/complete-profile before any tenant-scoped JWT is issued.
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleLoginAsync(request.IdToken, GetClientIp(), cancellationToken);
        if (result.ProfileCompletionRequired is not null)
        {
            return Ok(result.ProfileCompletionRequired);
        }

        RefreshTokenCookie.Set(Response, result.Tokens!.RawRefreshToken, _environment);
        return Ok(result.Tokens.Response);
    }

    /// <summary>
    /// US-15: the second half of a first-time Google signup. Requires an interim
    /// (profile_incomplete) token -- checked explicitly here, not just relied on
    /// structurally, even though an interim token's missing tenant_id/role claims already
    /// fail-close it out of every tenant-scoped/permission-gated endpoint on their own.
    /// </summary>
    [HttpPost("complete-profile")]
    public async Task<IActionResult> CompleteProfile(
        [FromBody] CompleteProfileRequest request, CancellationToken cancellationToken)
    {
        RequireTokenPurpose(TokenPurposes.ProfileIncomplete, "This endpoint requires a profile-completion token.");

        var result = await _authService.CompleteProfileAsync(GetUserIdClaim(), request, GetClientIp(), cancellationToken);
        RefreshTokenCookie.Set(Response, result.RawRefreshToken, _environment);
        return Ok(result.Response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, GetClientIp(), cancellationToken);
        return FromLoginResult(result);
    }

    /// <summary>
    /// US-24: the second half of a MustChangePassword-gated login. Requires a
    /// PasswordChangePending challenge token -- checked explicitly, not just relied on
    /// structurally.
    /// </summary>
    [HttpPost("change-temp-password")]
    public async Task<IActionResult> ChangeTempPassword(
        [FromBody] ChangeTempPasswordRequest request, CancellationToken cancellationToken)
    {
        RequireTokenPurpose(TokenPurposes.PasswordChangePending, "This endpoint requires a password-change challenge token.");

        var result = await _authService.ChangeTempPasswordAsync(
            GetUserIdClaim(), request.NewPassword, GetClientIp(), cancellationToken);
        return FromLoginResult(result);
    }

    /// <summary>US-17: the second half of a 2FA-gated login. Requires a TwoFactorPending
    /// challenge token -- checked explicitly, not just relied on structurally. Validates the
    /// submitted code directly against the code_hash/code_exp claims the challenge token
    /// carries (see IJwtTokenService.GenerateTwoFactorChallengeToken) rather than through
    /// ASP.NET Core Identity's built-in Email token provider.</summary>
    [HttpPost("login/verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor(
        [FromBody] VerifyTwoFactorRequest request, CancellationToken cancellationToken)
    {
        RequireTokenPurpose(TokenPurposes.TwoFactorPending, "This endpoint requires a two-factor challenge token.");

        if (!IsCodeValid(request.Code))
        {
            throw new UnauthorizedException("Invalid or expired code.");
        }

        var result = await _authService.VerifyTwoFactorAsync(GetUserIdClaim(), GetClientIp(), cancellationToken);
        RefreshTokenCookie.Set(Response, result.RawRefreshToken, _environment);
        return Ok(result.Response);
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            || string.IsNullOrEmpty(rawToken))
        {
            throw new UnauthorizedException("No refresh token presented.");
        }

        var outcome = await _authService.RefreshTokenAsync(rawToken, GetClientIp(), cancellationToken);
        if (!outcome.Success)
        {
            RefreshTokenCookie.Delete(Response);
            throw new UnauthorizedException(outcome.FailureMessage!);
        }

        RefreshTokenCookie.Set(Response, outcome.Tokens!.RawRefreshToken, _environment);
        return Ok(outcome.Tokens.Response);
    }

    [HttpPost("revoke-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeToken(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            && !string.IsNullOrEmpty(rawToken))
        {
            await _authService.RevokeAsync(rawToken, GetClientIp(), cancellationToken);
        }

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>
    /// Not part of US-02's literal acceptance criteria, but a near-universal need for any
    /// frontend consuming this API, and doubles as a live end-to-end check that
    /// PermissionClaimsTransformation (US-03) is actually expanding the role claim into a
    /// permission bundle -- the returned `permissions` array comes entirely from that
    /// transformation having already run before this action executes. Pure claims reading,
    /// no business logic or data access -- stays entirely in the controller.
    /// </summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirst("user_id")?.Value;
        var tenantId = User.FindFirst(TenantMiddleware.TenantIdClaimType)?.Value;
        var organizationId = User.FindFirst(TenantMiddleware.OrganizationIdClaimType)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var permissions = User.FindAll(PermissionClaimAuthorizationHandler.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

        return Ok(new
        {
            userId,
            tenantId,
            organizationId,
            role,
            permissions,
        });
    }

    private IActionResult FromLoginResult(LoginResult result)
    {
        if (result.PasswordChangeRequired is not null)
        {
            return Ok(result.PasswordChangeRequired);
        }

        if (result.TwoFactorRequired is not null)
        {
            return Ok(result.TwoFactorRequired);
        }

        RefreshTokenCookie.Set(Response, result.Tokens!.RawRefreshToken, _environment);
        return Ok(result.Tokens.Response);
    }

    private void RequireTokenPurpose(string expectedPurpose, string errorMessage)
    {
        var purpose = User.FindFirst(TokenPurposes.ClaimType)?.Value;
        if (purpose != expectedPurpose)
        {
            throw new ForbiddenException(errorMessage);
        }
    }

    private Guid GetUserIdClaim() => Guid.Parse(User.FindFirst("user_id")!.Value);

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>US-17 (fix): compares the submitted code's hash against the challenge
    /// token's code_hash claim (constant-time, to avoid a timing side-channel) and checks
    /// code_exp against the current time. False for anything malformed/missing rather than
    /// throwing -- an absent or unparsable claim on a token this endpoint already required
    /// to carry TokenPurposes.TwoFactorPending indicates a stale/foreign token, which is
    /// exactly what "invalid or expired code" already communicates. Pure claims-based
    /// validation with no DB access -- stays in the controller.</summary>
    private bool IsCodeValid(string submittedCode)
    {
        var codeHashClaim = User.FindFirst(TokenPurposes.CodeHashClaimType)?.Value;
        var codeExpiresClaim = User.FindFirst(TokenPurposes.CodeExpiresClaimType)?.Value;

        if (string.IsNullOrEmpty(codeHashClaim)
            || !long.TryParse(codeExpiresClaim, out var expiresUnixSeconds))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(expiresUnixSeconds))
        {
            return false;
        }

        byte[] storedHash;
        try
        {
            storedHash = Convert.FromHexString(codeHashClaim);
        }
        catch (FormatException)
        {
            return false; // malformed claim -- treat like any other invalid code
        }

        var submittedHash = SHA256.HashData(Encoding.UTF8.GetBytes(submittedCode));
        return storedHash.Length == submittedHash.Length
            && CryptographicOperations.FixedTimeEquals(submittedHash, storedHash);
    }
}
