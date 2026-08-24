using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// US-27: Switch-Context Test Coverage, through the real HTTP pipeline. OrganizationController
/// (US-04) had zero test coverage before this sprint despite being live since Phase 0 --
/// OrganizationControllerTests (unit) now covers SwitchContext's own logic in detail
/// (including the exact JWT claims a token carries), so this file is scoped to what only a
/// real HTTP round trip can prove: AddWorkspace + SwitchContext actually working together
/// end to end, and the cross-PM resident scenario (US-24's existing, never-yet-exercised-live
/// LinkExistingUserToTenantAsync path) switching successfully with zero backend changes.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class SwitchContextEndToEndTests : EmailIntegrationTestBase
{
    /// <summary>register(1) + login-as-PM(2) + verify-2fa(3) = 3 auth calls; AddWorkspace/
    /// SwitchContext/GetTenants/properties calls aren't under [EnableRateLimiting].</summary>
    [Fact]
    public async Task PropertyManager_CanAddAWorkspace_ThenSwitchIntoIt_AndActThere()
    {
        var pmEmail = "pm-for-switch-context@ten21.io";
        const string pmPassword = "Pm-Switch-Context-Passw0rd!1";
        var primaryAccessToken = await RegisterLoginAndVerifyAsync(pmEmail, pmPassword);

        var addWorkspaceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/organization/workspaces")
        {
            Content = JsonContent.Create(new { workspaceName = "Second Portfolio Property", portfolioSize = 2 }),
        };
        addWorkspaceRequest.Headers.Add("Authorization", $"Bearer {primaryAccessToken}");
        var addWorkspaceResponse = await Client.SendAsync(addWorkspaceRequest);
        Assert.Equal(HttpStatusCode.Created, addWorkspaceResponse.StatusCode);
        var newTenantId = (await ReadDataAsync(addWorkspaceResponse)).GetProperty("tenantId").GetGuid();

        var getTenantsResponse = await GetWithBearerAsync("/api/organization/tenants", primaryAccessToken);
        Assert.Equal(HttpStatusCode.OK, getTenantsResponse.StatusCode);
        using var tenantsDocument = JsonDocument.Parse(await getTenantsResponse.Content.ReadAsStringAsync());
        var tenantIds = tenantsDocument.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("tenantId").GetGuid()).ToList();
        Assert.Contains(newTenantId, tenantIds);

        var switchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/organization/switch-context")
        {
            Content = JsonContent.Create(new { tenantId = newTenantId }),
        };
        switchRequest.Headers.Add("Authorization", $"Bearer {primaryAccessToken}");
        var switchResponse = await Client.SendAsync(switchRequest);
        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);
        var switchedData = await ReadDataAsync(switchResponse);
        Assert.Equal(newTenantId, switchedData.GetProperty("tenantId").GetGuid());
        var scopedAccessToken = switchedData.GetProperty("accessToken").GetString()!;

        // A property created with the newly-switched token must land in the NEW workspace,
        // not the caller's original one -- proves the scoped token actually drives tenant
        // isolation, not just that it decodes with the right claim.
        var createPropertyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Property In The New Workspace",
                propertyType = "SingleFamily",
                streetAddress1 = "1 New Workspace Way",
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier = (string?)null,
                targetRent = (decimal?)null,
                occupancyStatus = "Vacant",
            }),
        };
        createPropertyRequest.Headers.Add("Authorization", $"Bearer {scopedAccessToken}");
        var createPropertyResponse = await Client.SendAsync(createPropertyRequest);
        Assert.Equal(HttpStatusCode.Created, createPropertyResponse.StatusCode);

        // The property is invisible under the ORIGINAL (still-valid) token -- confirms
        // tenant isolation held for the write, not just that the switch endpoint responded.
        var listUnderOriginalToken = await GetWithBearerAsync("/api/properties", primaryAccessToken);
        using var originalListDocument = JsonDocument.Parse(await listUnderOriginalToken.Content.ReadAsStringAsync());
        var namesUnderOriginal = originalListDocument.RootElement.GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.DoesNotContain("Property In The New Workspace", namesUnderOriginal);
    }

    /// <summary>register-pmA(1) + register-pmB(2) + login-as-resident(3) +
    /// change-temp-password(4) = 4 auth calls -- Register itself issues a full session
    /// directly (bypasses the 2FA gate entirely, see AuthController.Register's own
    /// IssueTokensAsync call), so PM setup doesn't need the separate login+verify-2fa dance
    /// the OTHER test in this file uses. The resident still goes through the real
    /// MustChangePassword gate (US-24) before getting a usable session -- Tenant isn't a
    /// MandatoryTwoFactorRoles member, so no separate verify-2fa call is needed on top of
    /// that. property/resident/switch/tenants calls aren't rate-limited.</summary>
    [Fact]
    public async Task ResidentAddedByTwoDifferentPropertyManagers_CanSwitchBetweenBothTenancies()
    {
        var pmAEmail = "pm-a-for-cross-pm-switch@ten21.io";
        var pmBEmail = "pm-b-for-cross-pm-switch@ten21.io";
        var residentEmail = "cross-pm-switch-resident@example.com";

        var pmAAccessToken = await RegisterAndGetAccessTokenAsync(pmAEmail, "Pm-A-Cross-Passw0rd!1", "PmA Cross Test Co");
        var propertyAId = await CreatePropertyAsync(pmAAccessToken, "100 PM-A St");
        await CreateResidentAsync(pmAAccessToken, propertyAId, residentEmail, "Cross");
        var welcomeFromA = Assert.Single(EmailSender.SentEmails, e => e.ToEmail == residentEmail);
        var temporaryPassword = ExtractTemporaryPassword(welcomeFromA.HtmlBody);
        EmailSender.SentEmails.Clear();

        var pmBAccessToken = await RegisterAndGetAccessTokenAsync(pmBEmail, "Pm-B-Cross-Passw0rd!1", "PmB Cross Test Co");
        var propertyBId = await CreatePropertyAsync(pmBAccessToken, "200 PM-B Ave");
        await CreateResidentAsync(pmBAccessToken, propertyBId, residentEmail, "Cross");

        // PM B's welcome attempt should have found the EXISTING account instead of resetting
        // its password -- confirms US-24's cross-PM linking, not a fresh provisioning.
        Assert.DoesNotContain(EmailSender.SentEmails, e => e.ToEmail == residentEmail && e.Subject.Contains("temporary password", StringComparison.OrdinalIgnoreCase));

        // Resident logs in with the ORIGINAL temp password from PM A -- still valid, since
        // PM B's linking never touched it. Still MustChangePassword=true (US-24), so this
        // is a PasswordChangeRequiredResponse, not a real session yet.
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { email = residentEmail, password = temporaryPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var changePasswordResponse = await PostWithBearerAsync(
            "/api/auth/change-temp-password", challengeToken, new { newPassword = "Cross-Pm-Resident-New-Passw0rd!1" });
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);
        var sessionData = await ReadDataAsync(changePasswordResponse);
        var accessToken = sessionData.GetProperty("accessToken").GetString()!;
        var primaryTenantId = sessionData.GetProperty("tenantId").GetGuid();

        var getTenantsResponse = await GetWithBearerAsync("/api/organization/tenants", accessToken);
        Assert.Equal(HttpStatusCode.OK, getTenantsResponse.StatusCode);
        using var tenantsDocument = JsonDocument.Parse(await getTenantsResponse.Content.ReadAsStringAsync());
        var tenantIds = tenantsDocument.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("tenantId").GetGuid()).ToList();
        Assert.Equal(2, tenantIds.Count); // one TenantMembership per PM's property

        var otherTenantId = tenantIds.Single(id => id != primaryTenantId);

        var switchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/organization/switch-context")
        {
            Content = JsonContent.Create(new { tenantId = otherTenantId }),
        };
        switchRequest.Headers.Add("Authorization", $"Bearer {accessToken}");
        var switchResponse = await Client.SendAsync(switchRequest);
        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);

        var switchedData = await ReadDataAsync(switchResponse);
        Assert.Equal(otherTenantId, switchedData.GetProperty("tenantId").GetGuid());
        Assert.Equal("Tenant", switchedData.GetProperty("role").GetString());
    }

    /// <summary>One call, not three -- Register issues a full session directly (see
    /// AuthController.Register's own IssueTokensAsync call), unlike Login which gates a
    /// mandatory-2FA role (PropertyManager, which every self-registration grants) behind a
    /// separate verify-2fa step. Only the OTHER test in this file needs the full
    /// register-then-login dance (to prove Login's own 2FA path specifically); this test
    /// only needs a working PM session as cheaply as possible.</summary>
    private async Task<string> RegisterAndGetAccessTokenAsync(string email, string password, string workspaceName)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Cross",
            lastName = "PmTest",
            email,
            password,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName,
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        return data.GetProperty("accessToken").GetString()!;
    }

    private async Task<string> RegisterLoginAndVerifyAsync(string email, string password)
    {
        await RegisterAsync(email, password, firstName: "Switch", lastName: "Context", workspaceName: "Switch Context Test Co");
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

    private async Task<string> CreatePropertyAsync(string accessToken, string streetAddress1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Switch Context Test Property",
                propertyType = "SingleFamily",
                streetAddress1,
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier = (string?)null,
                targetRent = (decimal?)null,
                occupancyStatus = "Vacant",
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await ReadDataAsync(response);
        return data.GetProperty("id").GetString()!;
    }

    private async Task CreateResidentAsync(string accessToken, string propertyId, string email, string firstName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/properties/{propertyId}/residents")
        {
            Content = JsonContent.Create(new
            {
                occupantType = "Primary",
                firstName,
                lastName = "Resident",
                email,
                phoneNumber = (string?)null,
                forwardingAddress = (string?)null,
                noticeGivenDate = (DateTimeOffset?)null,
                showInDirectory = false,
                emergencyContacts = Array.Empty<object>(),
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetWithBearerAsync(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        return await Client.SendAsync(request);
    }

    private static string ExtractTemporaryPassword(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "<strong>([^<]+)</strong>");
        Assert.True(match.Success, $"No temporary password found in welcome email body: {htmlBody}");
        return match.Groups[1].Value;
    }
}
