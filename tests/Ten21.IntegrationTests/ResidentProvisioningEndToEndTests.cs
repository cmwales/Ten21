using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// US-24: Zero-Token Tenant Welcome & Provisioning, proved through the real HTTP pipeline --
/// the one thing PropertiesEndToEndTests/ResidentsControllerTests (unit) can't fully exercise
/// is AuthController's own MustChangePassword login gate, since that lives in a different
/// controller than the one that creates the resident.
///
/// Flow: a PropertyManager registers, creates a property, adds a resident with an email
/// (provisioning fires) -> the resident logs in with the temp password captured out of the
/// actual welcome email -> gets PasswordChangeRequiredResponse (not a real session) ->
/// changes their password -> gets a real session with the Tenant role. Kept under
/// AuthRateLimiterPolicy's 5-req/min-per-IP budget: register(1) + login-as-PM(2) +
/// verify-2fa(3) + login-as-resident(4) + change-temp-password(5) = 5 auth calls
/// (create-property/create-resident aren't under [EnableRateLimiting], so they're free).
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class ResidentProvisioningEndToEndTests : EmailIntegrationTestBase
{
    [Fact]
    public async Task ResidentWithEmail_CanLoginWithTempPassword_ThenMustChangeItBeforeARealSessionIssues()
    {
        var pmEmail = "pm-for-resident-provisioning@ten21.io";
        const string pmPassword = "Pm-Provisioning-Passw0rd!1";
        var residentEmail = "provisioned-resident@example.com";

        var pmAccessToken = await RegisterLoginAndVerifyAsync(pmEmail, pmPassword);

        var propertyId = await CreatePropertyAsync(pmAccessToken);
        EmailSender.SentEmails.Clear(); // discard the PM's own 2FA email -- not what this proves

        var createResidentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/properties/{propertyId}/residents")
        {
            Content = JsonContent.Create(new
            {
                occupantType = "Primary",
                firstName = "Jamie",
                lastName = "Rivera",
                email = residentEmail,
                phoneNumber = (string?)null,
                forwardingAddress = (string?)null,
                noticeGivenDate = (DateTimeOffset?)null,
                showInDirectory = false,
                emergencyContacts = Array.Empty<object>(),
            }),
        };
        createResidentRequest.Headers.Add("Authorization", $"Bearer {pmAccessToken}");
        var createResidentResponse = await Client.SendAsync(createResidentRequest);
        Assert.Equal(HttpStatusCode.Created, createResidentResponse.StatusCode);

        var welcomeEmail = Assert.Single(EmailSender.SentEmails, e => e.ToEmail == residentEmail);
        var temporaryPassword = ExtractTemporaryPassword(welcomeEmail.HtmlBody);

        // First login attempt: correct temp password, but MustChangePassword short-circuits
        // straight into a password-change challenge -- no real session yet.
        var loginResponse = await Client.PostAsJsonAsync(
            "/api/auth/login", new { email = residentEmail, password = temporaryPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginData = await ReadDataAsync(loginResponse);
        Assert.True(loginData.GetProperty("requiresPasswordChange").GetBoolean());
        Assert.False(loginData.TryGetProperty("accessToken", out _)); // definitely not a real session
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var changePasswordResponse = await PostWithBearerAsync(
            "/api/auth/change-temp-password", challengeToken, new { newPassword = "Brand-New-Resident-Passw0rd!1" });
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var sessionData = await ReadDataAsync(changePasswordResponse);
        Assert.Equal("Tenant", sessionData.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(sessionData.GetProperty("accessToken").GetString()));
    }

    private async Task<string> RegisterLoginAndVerifyAsync(string email, string password)
    {
        await RegisterAsync(email, password, firstName: "Provisioning", lastName: "Test", workspaceName: "Provisioning Test Co");
        EmailSender.SentEmails.Clear();

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var sent = Assert.Single(EmailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
        var code = ExtractCode(sent.HtmlBody);

        var verifyResponse = await PostWithBearerAsync("/api/auth/login/verify-2fa", challengeToken, new { code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var verifyData = await ReadDataAsync(verifyResponse);
        return verifyData.GetProperty("accessToken").GetString()!;
    }

    private async Task<string> CreatePropertyAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Provisioning Test Property",
                propertyType = "SingleFamily",
                streetAddress1 = "900 Provisioning Way",
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier = (string?)null,
                targetRent = 1500m,
                occupancyStatus = "Vacant",
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var data = await ReadDataAsync(response);
        return data.GetProperty("id").GetString()!;
    }

    private static string ExtractTemporaryPassword(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "<strong>([^<]+)</strong>");
        Assert.True(match.Success, $"No temporary password found in welcome email body: {htmlBody}");
        return match.Groups[1].Value;
    }
}
