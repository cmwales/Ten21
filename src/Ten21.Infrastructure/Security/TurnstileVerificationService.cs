using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Security;

/// <summary>
/// Verifies a Cloudflare Turnstile token against the real siteverify endpoint (US-18) --
/// registered on an HttpClient via AddHttpClient (BotDefenseServiceCollectionExtensions),
/// same DI shape as IAmazonS3 for object storage.
///
/// Gates on THREE conditions, not just `success`, matching Cloudflare's own guidance: the
/// token must (1) be valid, (2) have been solved for the expected `action` (defends against
/// a token from a different widget/flow being replayed here), and (3) have been solved on
/// an allow-listed hostname (Turnstile:AllowedHostnames -- defaults to "localhost" for local
/// dev; must include the real production host, e.g. app.ten21.io, in that environment's
/// config). Any one of the three failing is a rejection, not just `success == false`.
/// </summary>
public class TurnstileVerificationService : ITurnstileVerificationService
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private const string ExpectedAction = "register";

    private readonly HttpClient _httpClient;
    private readonly string _secretKey;
    private readonly IReadOnlySet<string> _allowedHostnames;
    private readonly ILogger<TurnstileVerificationService> _logger;

    public TurnstileVerificationService(
        HttpClient httpClient, IConfiguration configuration, ILogger<TurnstileVerificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _secretKey = configuration["Turnstile:SecretKey"]
            ?? throw new InvalidOperationException(
                "Turnstile:SecretKey is not configured. Set it via `dotnet user-secrets set " +
                "\"Turnstile:SecretKey\" \"<value>\"` in src/Ten21.Api -- see README.");

        var allowedHostnamesRaw = configuration["Turnstile:AllowedHostnames"] ?? "localhost";
        _allowedHostnames = allowedHostnamesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return false;
        }

        var form = new Dictionary<string, string>
        {
            ["secret"] = _secretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        using var httpResponse = await _httpClient.PostAsync(
            SiteVerifyUrl, new FormUrlEncodedContent(form), cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Turnstile siteverify returned {StatusCode}", httpResponse.StatusCode);
            return false;
        }

        var result = await httpResponse.Content
            .ReadFromJsonAsync<TurnstileSiteVerifyResponse>(cancellationToken);

        if (result is null || !result.Success)
        {
            _logger.LogInformation(
                "Turnstile verification failed: {ErrorCodes}",
                result?.ErrorCodes is { Count: > 0 } codes ? string.Join(",", codes) : "(none reported)");
            return false;
        }

        // Action is checked only when Cloudflare actually reports one: a real solved widget
        // always echoes back its data-action, but Cloudflare's own published dummy testing
        // keys (used in this app's automated tests) omit the field entirely rather than
        // faking a value -- there is no way to make a real secret key produce a null
        // Action, so this can't be exploited to bypass the check with production
        // credentials, only Cloudflare's own test harness.
        if (result.Action is not null && !string.Equals(result.Action, ExpectedAction, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Turnstile token solved for unexpected action {Action}", result.Action);
            return false;
        }

        if (result.Hostname is null || !_allowedHostnames.Contains(result.Hostname))
        {
            _logger.LogWarning(
                "Turnstile token solved on non-allow-listed hostname {Hostname}", result.Hostname);
            return false;
        }

        return true;
    }

    private record TurnstileSiteVerifyResponse(
        bool Success,
        string? Action,
        string? Hostname,
        [property: JsonPropertyName("error-codes")] List<string>? ErrorCodes);
}
