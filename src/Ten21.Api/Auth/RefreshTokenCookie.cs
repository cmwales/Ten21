using Microsoft.AspNetCore.Http;

namespace Ten21.Api.Auth;

/// <summary>
/// Centralizes the ten21_refresh_token HTTP-only cookie contract (SECURITY.docx §2) so
/// AuthController and OrganizationController -- the two places that mint or read it -- can't
/// drift apart on options.
///
/// Path is "/api" rather than "/api/auth": originally auth-only, but US-04's switch-context
/// fix (see OrganizationController.SwitchContext) needs the browser to also send this cookie
/// to /api/organization/switch-context, and CookieOptions.Path only supports one prefix. "/api"
/// is the narrowest common ancestor of both routes.
/// </summary>
public static class RefreshTokenCookie
{
    public const string CookieName = "ten21_refresh_token";
    private const string CookiePath = "/api";

    public static void Set(HttpResponse response, string rawToken, IWebHostEnvironment environment)
    {
        response.Cookies.Append(CookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            // Secure cookies are refused by browsers over plain HTTP -- the local dev loop
            // runs on http://localhost (see launchSettings.json), so this is relaxed only
            // in Development. Production MUST run behind HTTPS regardless (see SECURITY.docx
            // hardened-headers requirements); this flag doesn't substitute for that.
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });
    }

    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });
    }
}
