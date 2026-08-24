using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// Regression coverage for a real reported bug: System.Text.Json's default enum handling
/// expects the numeric underlying value on the wire, not the string name -- so a request
/// body like {"propertyType":"SingleFamily"}, exactly what the Angular frontend (and any
/// real client) sends, was silently rejected with 400 before Program.cs registered a
/// JsonStringEnumConverter. Every existing PropertiesController unit test called the
/// controller action directly, bypassing JSON model binding entirely, so this went
/// undetected through Sprint 3's whole test suite -- only a live browser test against a
/// real backend caught it. This file exercises the controller through the real HTTP/JSON
/// pipeline (WebApplicationFactory, no shortcuts) specifically so that gap can't reopen.
///
/// Registration always yields PropertyManager (US-14), which is 2FA-mandatory, so getting
/// an authenticated session here requires the full register -> login -> verify-2fa dance
/// (see EmailIntegrationTestBase). Kept under AuthRateLimiterPolicy's 5-req/min-per-IP
/// budget: register(1) + login(2) + verify-2fa(3) + create-property(4) = 4 calls.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class PropertiesEndToEndTests : EmailIntegrationTestBase
{
    /// <summary>register(1) + login(2) + verify-2fa(3) + create-property(4) = 4 calls.</summary>
    [Fact]
    public async Task CreateProperty_WithStringEnumValuesOverRealJson_Succeeds()
    {
        var email = "properties-json-enum@ten21.io";
        const string password = "Properties-Json-Enum-Passw0rd!1";

        var accessToken = await RegisterLoginAndVerifyAsync(email, password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Riverside Apartments - Suite A",
                propertyType = "SingleFamily",
                streetAddress1 = "100 Main St",
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier = "Suite A",
                targetRent = 1200m,
                occupancyStatus = "Vacant",
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Expected success, got {response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("SingleFamily", data.GetProperty("propertyType").GetString());
        Assert.Equal("Vacant", data.GetProperty("occupancyStatus").GetString());
        Assert.Equal("Suite A", data.GetProperty("unitIdentifier").GetString());
    }

    private async Task<string> RegisterLoginAndVerifyAsync(string email, string password)
    {
        await RegisterAsync(email, password, firstName: "Properties", lastName: "Test", workspaceName: "Properties Json Enum Test Co");
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
}
