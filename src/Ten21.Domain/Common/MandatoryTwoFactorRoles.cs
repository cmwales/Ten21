namespace Ten21.Domain.Common;

/// <summary>
/// Roles SECURITY.docx §1 requires MFA for at every login, regardless of the individual
/// account's own TwoFactorEnabled preference (US-17). Every other role's MFA is
/// per-user opt-in (ApplicationUser.TwoFactorEnabled) -- SECURITY.docx's "Optional /
/// Adaptive MFA... for Residents by default." SECURITY.docx names TOTP apps specifically
/// for this mandatory case; US-17 delivers it via email OTP instead (no SMS gateway cost,
/// no authenticator-app lockout risk) -- TOTP stays available as an opt-in upgrade for any
/// role via POST /api/auth/2fa/totp/*, mandatory or not.
/// </summary>
public static class MandatoryTwoFactorRoles
{
    public static readonly IReadOnlyList<string> Values =
    [
        RoleNames.SuperAdmin, RoleNames.PropertyManager, RoleNames.BoardMember,
    ];
}
