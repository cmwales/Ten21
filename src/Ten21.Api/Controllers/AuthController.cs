using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ten21.Api.Auth;
using Ten21.Api.Contracts.Auth;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Middleware;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.RateLimiting;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-02: Identity & Refresh Token Pipeline.
///
/// Deliberately uses UserManager directly rather than SignInManager -- SignInManager is
/// built around cookie-based interactive sign-in flows; this is a stateless JWT API, and
/// UserManager.CheckPasswordAsync + manual AccessFailedAsync/ResetAccessFailedCountAsync
/// calls give the same brute-force lockout behavior (SECURITY.docx §5) without pulling in
/// cookie-auth-scheme machinery this API doesn't otherwise use.
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
    private const string GenericLoginFailureMessage = "Invalid email or password.";
    private const string GoogleLoginProvider = "Google";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Ten21DbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ITenantContext _tenantContext;
    private readonly ITurnstileVerificationService _turnstileVerificationService;
    private readonly IGoogleIdTokenVerifier _googleIdTokenVerifier;
    private readonly IEmailSender _emailSender;
    private readonly string _frontendBaseUrl;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        Ten21DbContext dbContext,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        ITenantContext tenantContext,
        ITurnstileVerificationService turnstileVerificationService,
        IGoogleIdTokenVerifier googleIdTokenVerifier,
        IEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _tenantContext = tenantContext;
        _turnstileVerificationService = turnstileVerificationService;
        _googleIdTokenVerifier = googleIdTokenVerifier;
        _emailSender = emailSender;
        _frontendBaseUrl = configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
        _environment = environment;
    }

    /// <summary>
    /// US-14: Workspace Registration & Onboarding. Creates the ApplicationUser, a brand-new
    /// Tenant (the "workspace"), and a root TenantMembership with BOTH PropertyManager and
    /// PropertyOwner claims -- a self-service landlord is both the operator and the deed
    /// owner of their own portfolio (see User_Stories_Phase_5.md for the full reasoning).
    /// Issues a full AuthResponse immediately (instant provisioning, no separate
    /// "now go log in" step) -- email confirmation (US-16) is a status flag layered on top
    /// later, never a login gate.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        // US-18: bot defense gates first, before any other validation runs -- no reason to
        // do field validation or an email-uniqueness DB lookup for a request that fails
        // this check anyway.
        if (!await _turnstileVerificationService.VerifyAsync(request.TurnstileToken, GetClientIp(), cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TurnstileToken)] = ["Bot verification failed. Please try again."],
            });
        }

        if (!request.AgreedToTerms)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.AgreedToTerms)] = ["You must agree to the Terms of Service to register."],
            });
        }

        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["An account with this email already exists."],
            });
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,
            EmailConfirmed = false,
            AgreedToTermsAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Password)] = createResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        var propertyManagerMembership = await ProvisionWorkspaceAsync(
            user.Id, request.WorkspaceName, request.PortfolioSize, cancellationToken);

        await SendActivationEmailAsync(user, cancellationToken);

        var response = await IssueTokensAsync(user.Id, propertyManagerMembership, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// US-16: sends a fresh tokenized confirmation link. Same-generic-response,
    /// enumeration-safe pattern as forgot-password -- true whether or not the account
    /// exists or is already confirmed, so a caller can't use this to probe either fact.
    /// </summary>
    [HttpPost("resend-activation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendActivation(
        [FromBody] ResendActivationRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null && !user.EmailConfirmed)
        {
            await SendActivationEmailAsync(user, cancellationToken);
        }

        return Ok(new GenericAcknowledgementResponse(
            "If that email exists and isn't already verified, we've sent a new confirmation link."));
    }

    /// <summary>US-16: confirms the account from an activation link's UserId + Token.</summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Token)] = ["This activation link is invalid or has expired."],
            });
        }

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Token)] = ["This activation link is invalid or has expired."],
            });
        }

        return Ok(new GenericAcknowledgementResponse("Your email has been verified."));
    }

    /// <summary>
    /// US-16: same enumeration-safe pattern as resend-activation -- identical response
    /// whether or not the email exists, so a caller can't use this to probe valid accounts.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"{_frontendBaseUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            await _emailSender.SendAsync(
                user.Email!,
                "Reset your Ten21 password",
                $"<p>Someone requested a password reset for this account. If this was you, " +
                $"click the link below (expires in 24 hours):</p><p><a href=\"{link}\">{link}</a></p>" +
                $"<p>If you didn't request this, you can safely ignore this email.</p>",
                cancellationToken);
        }

        return Ok(new GenericAcknowledgementResponse(
            "If that email exists, we've sent password reset instructions."));
    }

    /// <summary>
    /// US-16: resets the password from a reset link's UserId + Token, then revokes every
    /// other active session for the account (RevokeAllForUserAsync) -- a token issued under
    /// the old password shouldn't silently keep working after a reset.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Token)] = ["This password reset link is invalid or has expired."],
            });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.NewPassword)] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }

        await _refreshTokenService.RevokeAllForUserAsync(user.Id, GetClientIp(), cancellationToken);

        return Ok(new GenericAcknowledgementResponse("Your password has been reset. You can now log in."));
    }

    /// <summary>US-14/US-16: builds and sends the tokenized activation link. Shared by
    /// Register (first send) and ResendActivation (on-demand).</summary>
    private async Task SendActivationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{_frontendBaseUrl}/activate?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        await _emailSender.SendAsync(
            user.Email!,
            "Confirm your Ten21 account",
            $"<p>Welcome to Ten21! Confirm your email address by clicking the link below " +
            $"(expires in 24 hours):</p><p><a href=\"{link}\">{link}</a></p>",
            cancellationToken);
    }

    /// <summary>
    /// US-15: Google Sign-In. Verifies the Google ID token server-side, then either
    /// auto-links to an existing account (by prior Google login, falling back to a
    /// verified-email match) or creates a brand-new, passwordless ApplicationUser.
    /// A user with no workspace yet (either genuinely new, or an existing account that
    /// somehow has no TenantMembership) gets an interim token and must call
    /// POST /api/auth/complete-profile before any tenant-scoped JWT is issued.
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequest request, CancellationToken cancellationToken)
    {
        var identity = await _googleIdTokenVerifier.VerifyAsync(request.IdToken, cancellationToken);
        if (identity is null || !identity.EmailVerified)
        {
            throw new UnauthorizedException("Invalid Google credential.");
        }

        var user = await _userManager.FindByLoginAsync(GoogleLoginProvider, identity.Subject);

        if (user is null)
        {
            // No prior Google login recorded -- but a verified Google email matching an
            // existing account's email is enough to auto-link, per US-15's acceptance
            // criteria. Google already vouches for email ownership (EmailVerified above),
            // so this isn't trusting client input the way it would be for an unverified
            // claim.
            user = await _userManager.FindByEmailAsync(identity.Email);
            if (user is not null)
            {
                var linkResult = await _userManager.AddLoginAsync(
                    user, new UserLoginInfo(GoogleLoginProvider, identity.Subject, GoogleLoginProvider));
                if (!linkResult.Succeeded)
                {
                    throw new ValidationException(new Dictionary<string, string[]>
                    {
                        ["Google"] = linkResult.Errors.Select(e => e.Description).ToArray(),
                    });
                }
            }
        }

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = identity.Email,
                Email = identity.Email,
                EmailConfirmed = true, // Google already verified it -- US-16 has nothing to add here
                FirstName = string.IsNullOrWhiteSpace(identity.GivenName) ? "New" : identity.GivenName,
                LastName = string.IsNullOrWhiteSpace(identity.FamilyName) ? "User" : identity.FamilyName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            // No password -- CheckPasswordAsync will simply always fail for this account,
            // which is correct: Google Sign-In is the only way in for a Google-only user.
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["Email"] = createResult.Errors.Select(e => e.Description).ToArray(),
                });
            }

            await _userManager.AddLoginAsync(
                user, new UserLoginInfo(GoogleLoginProvider, identity.Subject, GoogleLoginProvider));
        }

        var hasWorkspace = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .AnyAsync(tm => tm.UserId == user.Id, cancellationToken);

        if (!hasWorkspace)
        {
            var interimToken = _jwtTokenService.GenerateInterimAccessToken(user.Id, TokenPurposes.ProfileIncomplete);
            return Ok(new ProfileCompletionRequiredResponse(true, interimToken.Value, interimToken.ExpiresAtUtc));
        }

        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken)
            ?? throw new UnauthorizedException("This account has no property/tenant access configured.");

        var response = await IssueTokensAsync(user.Id, membership, cancellationToken);
        return Ok(response);
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
        var purpose = User.FindFirst(TokenPurposes.ClaimType)?.Value;
        if (purpose != TokenPurposes.ProfileIncomplete)
        {
            throw new ForbiddenException("This endpoint requires a profile-completion token.");
        }

        var userId = Guid.Parse(User.FindFirst("user_id")!.Value);

        var alreadyHasWorkspace = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .AnyAsync(tm => tm.UserId == userId, cancellationToken);
        if (alreadyHasWorkspace)
        {
            throw new ConflictException("This account already has a workspace.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedException("Account no longer exists.");

        user.PhoneNumber = request.PhoneNumber;
        user.Address = request.Address;
        await _userManager.UpdateAsync(user);

        var propertyManagerMembership = await ProvisionWorkspaceAsync(
            userId, request.WorkspaceName, request.PortfolioSize, cancellationToken);

        var response = await IssueTokensAsync(userId, propertyManagerMembership, cancellationToken);
        return Ok(response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Same generic message regardless of WHICH check fails (no such user, wrong
        // password, inactive account) -- distinguishing them would let a caller enumerate
        // valid email addresses one login attempt at a time.
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException(GenericLoginFailureMessage);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException(
                "Account is temporarily locked due to repeated failed login attempts.");
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user); // counts toward the 5-attempt lockout
            throw new UnauthorizedException(GenericLoginFailureMessage);
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        // US-24: checked BEFORE membership/2FA -- an account provisioned with a temporary
        // password (e.g. a resident invited via ResidentsController) must change it before
        // anything else happens, including a 2FA challenge for a role that would otherwise
        // need one. Mirrors US-17's 2FA challenge-token pattern, but reuses
        // GenerateInterimAccessToken directly (US-15's own mechanism) rather than a new
        // dedicated method -- there's no "code" here, just a boolean gate.
        if (user.MustChangePassword)
        {
            var passwordChallenge = _jwtTokenService.GenerateInterimAccessToken(user.Id, TokenPurposes.PasswordChangePending);
            return Ok(new PasswordChangeRequiredResponse(true, passwordChallenge.Value, passwordChallenge.ExpiresAtUtc));
        }

        return await CompleteLoginAsync(user, cancellationToken);
    }

    /// <summary>
    /// The shared tail of both a normal Login (once MustChangePassword is confirmed false)
    /// and ChangeTempPassword's successful completion: resolve tenant membership, gate on
    /// mandatory 2FA if this role requires it, otherwise issue a real session.
    /// </summary>
    private async Task<IActionResult> CompleteLoginAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedException("This account has no property/tenant access configured.");
        }

        // US-17 (email-only, per Founder decision -- TOTP/authenticator-app support was
        // built and then deliberately removed): password alone isn't enough for
        // SuperAdmin/PropertyManager/BoardMember (SECURITY.md §1's mandatory-MFA roles).
        // Resolve the role BEFORE issuing tokens so this check can run ahead of
        // IssueTokensAsync, not after.
        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException(
                $"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        if (MandatoryTwoFactorRoles.Values.Contains(role.Name))
        {
            // A fresh, cryptographically random code every call -- no dependency on
            // Identity's built-in Email token provider, whose ~3-minute TOTP step is
            // hardcoded (not configurable) and returns the SAME code for repeated calls
            // within that step. codeExpiresAtUtc is this code's own, real 5-minute window,
            // independent of the challenge token's own (longer) lifetime -- see
            // GenerateTwoFactorChallengeToken's doc comment.
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
            var codeExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);

            await _emailSender.SendAsync(
                user.Email!,
                "Your Ten21 sign-in code",
                $"<p>Your sign-in code is: <strong>{code}</strong></p>" +
                $"<p>Enter it to finish signing in. This code expires in 5 minutes.</p>",
                cancellationToken);

            var challenge = _jwtTokenService.GenerateTwoFactorChallengeToken(user.Id, codeHash, codeExpiresAtUtc);
            return Ok(new TwoFactorRequiredResponse(true, challenge.Value, challenge.ExpiresAtUtc));
        }

        var response = await IssueTokensAsync(user.Id, membership, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// US-24: the second half of a MustChangePassword-gated login. Requires a
    /// PasswordChangePending challenge token -- checked explicitly, not just relied on
    /// structurally (same defensive pattern as VerifyTwoFactor). No CurrentPassword field on
    /// the request: the challenge token itself already proves knowledge of the current
    /// (temporary) password. Uses RemovePasswordAsync + AddPasswordAsync rather than
    /// ChangePasswordAsync for exactly that reason -- there's nothing left to re-verify.
    /// </summary>
    [HttpPost("change-temp-password")]
    public async Task<IActionResult> ChangeTempPassword(
        [FromBody] ChangeTempPasswordRequest request, CancellationToken cancellationToken)
    {
        var purpose = User.FindFirst(TokenPurposes.ClaimType)?.Value;
        if (purpose != TokenPurposes.PasswordChangePending)
        {
            throw new ForbiddenException("This endpoint requires a password-change challenge token.");
        }

        var user = await GetCurrentUserAsync();

        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["NewPassword"] = removeResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        var addResult = await _userManager.AddPasswordAsync(user, request.NewPassword);
        if (!addResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["NewPassword"] = addResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);

        return await CompleteLoginAsync(user, cancellationToken);
    }

    /// <summary>US-17: the second half of a 2FA-gated login. Requires a TwoFactorPending
    /// challenge token -- checked explicitly, not just relied on structurally. Validates the
    /// submitted code directly against the code_hash/code_exp claims the challenge token
    /// carries (see GenerateTwoFactorChallengeToken) rather than through ASP.NET Core
    /// Identity's built-in Email token provider.</summary>
    [HttpPost("login/verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor(
        [FromBody] VerifyTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var purpose = User.FindFirst(TokenPurposes.ClaimType)?.Value;
        if (purpose != TokenPurposes.TwoFactorPending)
        {
            throw new ForbiddenException("This endpoint requires a two-factor challenge token.");
        }

        if (!IsCodeValid(request.Code))
        {
            throw new UnauthorizedException("Invalid or expired code.");
        }

        var user = await GetCurrentUserAsync();

        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken)
            ?? throw new UnauthorizedException("This account has no property/tenant access configured.");

        var response = await IssueTokensAsync(user.Id, membership, cancellationToken);
        return Ok(response);
    }

    /// <summary>US-17 (fix): compares the submitted code's hash against the challenge
    /// token's code_hash claim (constant-time, to avoid a timing side-channel) and checks
    /// code_exp against the current time. False for anything malformed/missing rather than
    /// throwing -- an absent or unparsable claim on a token this endpoint already required
    /// to carry TokenPurposes.TwoFactorPending indicates a stale/foreign token, which is
    /// exactly what "invalid or expired code" already communicates.</summary>
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

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            || string.IsNullOrEmpty(rawToken))
        {
            throw new UnauthorizedException("No refresh token presented.");
        }

        RefreshTokenRotationResult rotation;
        try
        {
            rotation = await _refreshTokenService.ValidateAndRotateAsync(rawToken, GetClientIp(), cancellationToken);
        }
        catch (RefreshTokenException ex)
        {
            RefreshTokenCookie.Delete(Response);
            throw new UnauthorizedException(ex.Message);
        }

        // IgnoreQueryFilters is required and deliberate: no ITenantContext is resolved for
        // this request (there's no valid access token yet -- that's the entire point of
        // refresh), so the fail-closed filter from US-01 would otherwise return zero rows.
        //
        // A list, not SingleOrDefaultAsync: SECURITY.docx explicitly supports a user holding
        // more than one role in the SAME tenant (e.g. an Owner who is also a Board Member;
        // as of US-14, every self-service registrant is both PropertyManager and
        // PropertyOwner on their own workspace). Prefer whichever membership is IsPrimary,
        // same tie-break Login already uses, so a refreshed token keeps the same role the
        // original login issued.
        var memberships = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .Where(tm => tm.UserId == rotation.UserId && tm.TenantId == rotation.TenantId)
            .ToListAsync(cancellationToken);

        var membership = memberships.FirstOrDefault(m => m.IsPrimary) ?? memberships.FirstOrDefault();

        if (membership is null)
        {
            // Membership was revoked between issuance and this refresh (e.g. an admin
            // removed this person from the property). The old refresh token was already
            // consumed by ValidateAndRotateAsync above; deny the new access token too.
            RefreshTokenCookie.Delete(Response);
            throw new UnauthorizedException("Tenant access for this account has changed. Please log in again.");
        }

        var response = await BuildAuthResponseAsync(
            rotation.UserId, membership, rotation.NewRawToken, cancellationToken);
        return Ok(response);
    }

    [HttpPost("revoke-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeToken(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            && !string.IsNullOrEmpty(rawToken))
        {
            await _refreshTokenService.RevokeAsync(rawToken, GetClientIp(), cancellationToken);
        }

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>
    /// Not part of US-02's literal acceptance criteria, but a near-universal need for any
    /// frontend consuming this API, and doubles as a live end-to-end check that
    /// PermissionClaimsTransformation (US-03) is actually expanding the role claim into a
    /// permission bundle -- the returned `permissions` array comes entirely from that
    /// transformation having already run before this action executes.
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

    /// <summary>
    /// Shared by US-14 (Register) and US-15 (CompleteProfile): creates the new Tenant
    /// (workspace) and a root TenantMembership pair -- PropertyManager (IsPrimary = true)
    /// and PropertyOwner (IsPrimary = false) -- for userId. See Register's doc comment for
    /// why a self-service landlord gets both roles.
    /// </summary>
    private async Task<TenantMembership> ProvisionWorkspaceAsync(
        Guid userId, string workspaceName, int portfolioSize, CancellationToken cancellationToken)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = workspaceName,
            PortfolioSize = portfolioSize,
            OrganizationId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Tenant is not ITenantScopedEntity (it sits above the tenant boundary) -- no active
        // tenant context is needed for its own insert.
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var propertyManagerRole = await _roleManager.FindByNameAsync(RoleNames.PropertyManager)
            ?? throw new InvalidOperationException("PropertyManager role not seeded -- has RoleSeeder run?");
        var propertyOwnerRole = await _roleManager.FindByNameAsync(RoleNames.PropertyOwner)
            ?? throw new InvalidOperationException("PropertyOwner role not seeded -- has RoleSeeder run?");

        // TenantMembership IS ITenantScopedEntity -- the fail-closed insert guard (US-01)
        // requires an active tenant context before either row below can be added.
        _tenantContext.SetTenant(tenant.Id);

        var propertyManagerMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            RoleId = propertyManagerRole.Id,
            IsPrimary = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.TenantMemberships.Add(propertyManagerMembership);
        _dbContext.TenantMemberships.Add(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            RoleId = propertyOwnerRole.Id,
            IsPrimary = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return propertyManagerMembership;
    }

    private async Task<TenantMembership?> ResolvePrimaryMembershipAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.TenantMemberships
            .IgnoreQueryFilters() // same bootstrap reasoning as the refresh-token lookup above
            .Where(tm => tm.UserId == userId)
            .ToListAsync(cancellationToken);

        return memberships.FirstOrDefault(m => m.IsPrimary) ?? memberships.FirstOrDefault();
    }

    private async Task<AuthResponse> IssueTokensAsync(
        Guid userId, TenantMembership membership, CancellationToken cancellationToken)
    {
        var rawRefreshToken = await _refreshTokenService.IssueAsync(
            userId, membership.TenantId, GetClientIp(), cancellationToken);

        return await BuildAuthResponseAsync(userId, membership, rawRefreshToken, cancellationToken);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(
        Guid userId, TenantMembership membership, string rawRefreshToken, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException($"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters() // Tenant isn't ITenantScopedEntity, but be explicit rather than rely on that
            .SingleAsync(t => t.Id == membership.TenantId, cancellationToken);

        var accessToken = _jwtTokenService.GenerateAccessToken(
            userId, membership.TenantId, tenant.OrganizationId, role.Name!);

        RefreshTokenCookie.Set(Response, rawRefreshToken, _environment);

        return new AuthResponse(
            accessToken.Value, accessToken.ExpiresAtUtc, membership.TenantId, tenant.OrganizationId, role.Name!);
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>US-15/US-17 shared helper: resolves the caller's ApplicationUser from the
    /// user_id claim, valid for both full sessions and interim (profile-incomplete /
    /// 2fa-pending) tokens alike, since both carry that one claim.</summary>
    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var userId = User.FindFirst("user_id")?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing the user_id claim.");

        return await _userManager.FindByIdAsync(userId)
            ?? throw new UnauthorizedException("Account no longer exists.");
    }
}
