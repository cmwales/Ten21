using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// US-25: Tenant Access &amp; Directory Privacy, proved through the real HTTP pipeline now
/// that US-24 makes a real Tenant-role login actually obtainable (previously this hard-block
/// was true "by construction" -- RolePermissions never granted Tenant any Property.*/
/// Resident.* claim -- but nothing could exercise it live because no real Tenant session
/// existed yet).
///
/// Flow: a PM registers, creates two properties sharing an address (both
/// AllowTenantDirectory = true), adds a resident to each (ShowInDirectory = true on both) --
/// one of them (the caller) logs all the way in through the real MustChangePassword gate.
/// Then: (1) that Tenant-role session gets 403 on every PropertiesController/
/// ResidentsController action, confirming the existing hard-block still holds; (2) it gets
/// 200 on the new directory endpoint, seeing its sibling's entry but never its own.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class TenantAccessEndToEndTests : EmailIntegrationTestBase
{
    /// <summary>register(1) + login-as-PM(2) + verify-2fa(3) + login-as-resident(4) +
    /// change-temp-password(5) = 5 auth calls; property/resident/directory calls aren't
    /// under [EnableRateLimiting].</summary>
    [Fact]
    public async Task TenantSession_IsBlockedFromPropertyManagement_ButCanSeeTheOptedInDirectory()
    {
        var pmEmail = "pm-for-tenant-access@ten21.io";
        const string pmPassword = "Pm-Tenant-Access-Passw0rd!1";
        var residentAEmail = "tenant-access-resident-a@example.com";
        var residentBEmail = "tenant-access-resident-b@example.com";

        var pmAccessToken = await RegisterLoginAndVerifyAsync(pmEmail, pmPassword);

        var propertyAId = await CreatePropertyAsync(pmAccessToken, unitIdentifier: "Suite A");
        var propertyBId = await CreatePropertyAsync(pmAccessToken, unitIdentifier: "Suite B");
        EmailSender.SentEmails.Clear();

        await CreateResidentAsync(pmAccessToken, propertyAId, residentAEmail, "Alex");
        await CreateResidentAsync(pmAccessToken, propertyBId, residentBEmail, "Blair");

        var welcomeEmail = Assert.Single(EmailSender.SentEmails, e => e.ToEmail == residentAEmail);
        var temporaryPassword = ExtractTemporaryPassword(welcomeEmail.HtmlBody);

        var loginResponse = await Client.PostAsJsonAsync(
            "/api/auth/login", new { email = residentAEmail, password = temporaryPassword });
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var changePasswordResponse = await PostWithBearerAsync(
            "/api/auth/change-temp-password", challengeToken, new { newPassword = "Tenant-A-New-Passw0rd!1" });
        var residentAccessToken = (await ReadDataAsync(changePasswordResponse)).GetProperty("accessToken").GetString()!;

        // --- (1) the existing hard-block, now provable with a real Tenant session ---
        var getPropertiesResponse = await GetWithBearerAsync("/api/properties", residentAccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, getPropertiesResponse.StatusCode);

        var createPropertyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Should Not Be Creatable",
                propertyType = "SingleFamily",
                streetAddress1 = "1 Nope St",
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
        createPropertyRequest.Headers.Add("Authorization", $"Bearer {residentAccessToken}");
        var createPropertyResponse = await Client.SendAsync(createPropertyRequest);
        Assert.Equal(HttpStatusCode.Forbidden, createPropertyResponse.StatusCode);

        var getResidentsResponse = await GetWithBearerAsync($"/api/properties/{propertyAId}/residents", residentAccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, getResidentsResponse.StatusCode);

        // --- (2) the dual-consent directory ---
        var directoryResponse = await GetWithBearerAsync("/api/directory", residentAccessToken);
        Assert.Equal(HttpStatusCode.OK, directoryResponse.StatusCode);

        using var directoryDocument = JsonDocument.Parse(await directoryResponse.Content.ReadAsStringAsync());
        var entries = directoryDocument.RootElement.GetProperty("data").EnumerateArray().ToList();

        var entry = Assert.Single(entries);
        Assert.Equal("Blair", entry.GetProperty("firstName").GetString());
        Assert.Equal("Suite B", entry.GetProperty("unitIdentifier").GetString());
        Assert.DoesNotContain(entries, e => e.GetProperty("firstName").GetString() == "Alex"); // never the caller's own entry
    }

    private async Task<string> RegisterLoginAndVerifyAsync(string email, string password)
    {
        await RegisterAsync(email, password, firstName: "TenantAccess", lastName: "Test", workspaceName: "Tenant Access Test Co");
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

    private async Task<string> CreatePropertyAsync(string accessToken, string unitIdentifier)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Tenant Access Test Property",
                propertyType = "MultiFamily",
                streetAddress1 = "800 Directory Ln",
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier,
                targetRent = 1200m,
                occupancyStatus = "Occupied",
                allowTenantDirectory = true,
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
                showInDirectory = true,
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
