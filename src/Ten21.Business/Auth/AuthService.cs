using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Auth;

/// <summary>
/// US-02: Identity & Refresh Token Pipeline.
///
/// Business-layer refactor: all business logic AND all data access now live here --
/// AuthController has no Ten21DbContext dependency at all. Everything that genuinely
/// requires the live HTTP request (ClaimsPrincipal claim reads, the refresh-token cookie
/// itself, HttpContext.Connection.RemoteIpAddress) stays in the controller and is passed in
/// here as plain values (Guid userId, string? clientIp, a raw token string, etc).
///
/// Deliberately uses UserManager directly rather than SignInManager -- SignInManager is
/// built around cookie-based interactive sign-in flows; this is a stateless JWT API, and
/// UserManager.CheckPasswordAsync + manual AccessFailedAsync/ResetAccessFailedCountAsync
/// calls give the same brute-force lockout behavior (SECURITY.docx §5) without pulling in
/// cookie-auth-scheme machinery this API doesn't otherwise use.
/// </summary>
public class AuthService
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

    public AuthService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        Ten21DbContext dbContext,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        ITenantContext tenantContext,
        ITurnstileVerificationService turnstileVerificationService,
        IGoogleIdTokenVerifier googleIdTokenVerifier,
        IEmailSender emailSender,
        IConfiguration configuration)
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
    }

    /// <summary>
    /// US-14: Workspace Registration & Onboarding. Creates the ApplicationUser, a brand-new
    /// Tenant (the "workspace"), and a root TenantMembership with BOTH PropertyManager and
    /// PropertyOwner claims -- a self-service landlord is both the operator and the deed
    /// owner of their own portfolio.
    /// </summary>
    public async Task<AuthTokenResult> RegisterAsync(
        RegisterRequest request, string? clientIp, CancellationToken cancellationToken)
    {
        // US-18: bot defense gates first, before any other validation runs.
        if (!await _turnstileVerificationService.VerifyAsync(request.TurnstileToken, clientIp, cancellationToken))
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

        return await IssueTokensAsync(user.Id, propertyManagerMembership, clientIp, cancellationToken);
    }

    /// <summary>
    /// US-16: sends a fresh tokenized confirmation link. Same-generic-response,
    /// enumeration-safe pattern as forgot-password.
    /// </summary>
    public async Task ResendActivationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null && !user.EmailConfirmed)
        {
            await SendActivationEmailAsync(user, cancellationToken);
        }
    }

    /// <summary>US-16: confirms the account from an activation link's UserId + Token.</summary>
    public async Task ActivateAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(token)] = ["This activation link is invalid or has expired."],
            });
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(token)] = ["This activation link is invalid or has expired."],
            });
        }
    }

    /// <summary>
    /// US-16: same enumeration-safe pattern as resend-activation -- identical outcome
    /// whether or not the email exists, so a caller can't use this to probe valid accounts.
    /// </summary>
    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(email);
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
    }

    /// <summary>
    /// US-16: resets the password from a reset link's UserId + Token, then revokes every
    /// other active session for the account -- a token issued under the old password
    /// shouldn't silently keep working after a reset.
    /// </summary>
    public async Task ResetPasswordAsync(
        Guid userId, string token, string newPassword, string? clientIp, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Token"] = ["This password reset link is invalid or has expired."],
            });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["NewPassword"] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }

        await _refreshTokenService.RevokeAllForUserAsync(user.Id, clientIp, cancellationToken);
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
    /// verified-email match) or creates a brand-new, passwordless ApplicationUser. A user
    /// with no workspace yet gets an interim token instead of a full AuthResponse.
    /// </summary>
    public async Task<GoogleLoginResult> GoogleLoginAsync(
        string idToken, string? clientIp, CancellationToken cancellationToken)
    {
        var identity = await _googleIdTokenVerifier.VerifyAsync(idToken, cancellationToken);
        if (identity is null || !identity.EmailVerified)
        {
            throw new UnauthorizedException("Invalid Google credential.");
        }

        var user = await _userManager.FindByLoginAsync(GoogleLoginProvider, identity.Subject);

        if (user is null)
        {
            // No prior Google login recorded -- but a verified Google email matching an
            // existing account's email is enough to auto-link. Google already vouches for
            // email ownership (EmailVerified above), so this isn't trusting client input the
            // way it would be for an unverified claim.
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
                EmailConfirmed = true, // Google already verified it
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
            return GoogleLoginResult.ForProfileCompletion(
                new ProfileCompletionRequiredResponse(true, interimToken.Value, interimToken.ExpiresAtUtc));
        }

        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken)
            ?? throw new UnauthorizedException("This account has no property/tenant access configured.");

        var tokens = await IssueTokensAsync(user.Id, membership, clientIp, cancellationToken);
        return GoogleLoginResult.ForTokens(tokens);
    }

    /// <summary>US-15: the second half of a first-time Google signup. The controller has
    /// already checked the caller presented a profile_incomplete interim token before
    /// calling this.</summary>
    public async Task<AuthTokenResult> CompleteProfileAsync(
        Guid userId, CompleteProfileRequest request, string? clientIp, CancellationToken cancellationToken)
    {
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

        return await IssueTokensAsync(userId, propertyManagerMembership, clientIp, cancellationToken);
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request, string? clientIp, CancellationToken cancellationToken)
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
        // password must change it before anything else happens, including a 2FA challenge.
        if (user.MustChangePassword)
        {
            var passwordChallenge = _jwtTokenService.GenerateInterimAccessToken(user.Id, TokenPurposes.PasswordChangePending);
            return LoginResult.ForPasswordChange(
                new PasswordChangeRequiredResponse(true, passwordChallenge.Value, passwordChallenge.ExpiresAtUtc));
        }

        return await CompleteLoginAsync(user, clientIp, cancellationToken);
    }

    /// <summary>
    /// The shared tail of both a normal Login (once MustChangePassword is confirmed false)
    /// and ChangeTempPassword's successful completion: resolve tenant membership, gate on
    /// mandatory 2FA if this role requires it, otherwise issue a real session.
    /// </summary>
    private async Task<LoginResult> CompleteLoginAsync(
        ApplicationUser user, string? clientIp, CancellationToken cancellationToken)
    {
        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedException("This account has no property/tenant access configured.");
        }

        // US-17 (email-only, per Founder decision): password alone isn't enough for
        // SuperAdmin/PropertyManager/BoardMember (SECURITY.md §1's mandatory-MFA roles).
        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException(
                $"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        if (MandatoryTwoFactorRoles.Values.Contains(role.Name))
        {
            // A fresh, cryptographically random code every call -- no dependency on
            // Identity's built-in Email token provider, whose ~3-minute TOTP step is
            // hardcoded and returns the SAME code for repeated calls within that step.
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
            return LoginResult.ForTwoFactor(new TwoFactorRequiredResponse(true, challenge.Value, challenge.ExpiresAtUtc));
        }

        var tokens = await IssueTokensAsync(user.Id, membership, clientIp, cancellationToken);
        return LoginResult.ForTokens(tokens);
    }

    /// <summary>
    /// US-24: the second half of a MustChangePassword-gated login. The controller has
    /// already checked the caller presented a PasswordChangePending challenge token. No
    /// CurrentPassword field -- the challenge token itself already proves knowledge of the
    /// current (temporary) password. Uses RemovePasswordAsync + AddPasswordAsync rather than
    /// ChangePasswordAsync for exactly that reason -- there's nothing left to re-verify.
    /// </summary>
    public async Task<LoginResult> ChangeTempPasswordAsync(
        Guid userId, string newPassword, string? clientIp, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId);

        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["NewPassword"] = removeResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        var addResult = await _userManager.AddPasswordAsync(user, newPassword);
        if (!addResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["NewPassword"] = addResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);

        return await CompleteLoginAsync(user, clientIp, cancellationToken);
    }

    /// <summary>US-17: the second half of a 2FA-gated login. The controller has already
    /// validated the submitted code against the challenge token's code_hash/code_exp claims
    /// before calling this.</summary>
    public async Task<AuthTokenResult> VerifyTwoFactorAsync(
        Guid userId, string? clientIp, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId);

        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken)
            ?? throw new UnauthorizedException("This account has no property/tenant access configured.");

        return await IssueTokensAsync(user.Id, membership, clientIp, cancellationToken);
    }

    public async Task<RefreshTokenOutcome> RefreshTokenAsync(
        string rawToken, string? clientIp, CancellationToken cancellationToken)
    {
        RefreshTokenRotationResult rotation;
        try
        {
            rotation = await _refreshTokenService.ValidateAndRotateAsync(rawToken, clientIp, cancellationToken);
        }
        catch (RefreshTokenException ex)
        {
            return RefreshTokenOutcome.Failed(ex.Message);
        }

        // IgnoreQueryFilters is required and deliberate: no ITenantContext is resolved for
        // this request (there's no valid access token yet -- that's the entire point of
        // refresh), so the fail-closed filter from US-01 would otherwise return zero rows.
        //
        // A list, not SingleOrDefaultAsync: SECURITY.docx explicitly supports a user holding
        // more than one role in the SAME tenant. Prefer whichever membership is IsPrimary,
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
            return RefreshTokenOutcome.Failed("Tenant access for this account has changed. Please log in again.");
        }

        var response = await BuildAuthResponseAsync(rotation.UserId, membership, cancellationToken);
        return RefreshTokenOutcome.Succeeded(new AuthTokenResult(response, rotation.NewRawToken));
    }

    public Task RevokeAsync(string rawToken, string? clientIp, CancellationToken cancellationToken) =>
        _refreshTokenService.RevokeAsync(rawToken, clientIp, cancellationToken);

    /// <summary>
    /// Shared by Register and CompleteProfile: creates the new Tenant (workspace) and a
    /// root TenantMembership pair -- PropertyManager (IsPrimary = true) and PropertyOwner
    /// (IsPrimary = false) -- for userId. See Register's doc comment for why a self-service
    /// landlord gets both roles.
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

    private async Task<AuthTokenResult> IssueTokensAsync(
        Guid userId, TenantMembership membership, string? clientIp, CancellationToken cancellationToken)
    {
        var rawRefreshToken = await _refreshTokenService.IssueAsync(
            userId, membership.TenantId, clientIp, cancellationToken);

        var response = await BuildAuthResponseAsync(userId, membership, cancellationToken);
        return new AuthTokenResult(response, rawRefreshToken);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(
        Guid userId, TenantMembership membership, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException($"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters() // Tenant isn't ITenantScopedEntity, but be explicit rather than rely on that
            .SingleAsync(t => t.Id == membership.TenantId, cancellationToken);

        var accessToken = _jwtTokenService.GenerateAccessToken(
            userId, membership.TenantId, tenant.OrganizationId, role.Name!);

        return new AuthResponse(
            accessToken.Value, accessToken.ExpiresAtUtc, membership.TenantId, tenant.OrganizationId, role.Name!);
    }

    /// <summary>US-15/US-17/US-24 shared helper: resolves an ApplicationUser by id for a
    /// caller the controller has already authenticated via an interim token's user_id
    /// claim.</summary>
    private async Task<ApplicationUser> GetUserAsync(Guid userId) =>
        await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedException("Account no longer exists.");
}
