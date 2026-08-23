namespace Ten21.Domain.Common;

/// <summary>
/// Roles SECURITY.docx §1 requires MFA for at every login (US-17). SECURITY.docx names TOTP
/// apps for this; the actual implementation is email OTP instead, by deliberate Founder
/// decision (no SMS gateway cost, no authenticator-app lockout risk, and no separate
/// enrollment step to build/maintain) -- there is no TOTP/authenticator-app option at all.
/// </summary>
public static class MandatoryTwoFactorRoles
{
    public static readonly IReadOnlyList<string> Values =
    [
        RoleNames.SuperAdmin, RoleNames.PropertyManager, RoleNames.BoardMember,
    ];
}
