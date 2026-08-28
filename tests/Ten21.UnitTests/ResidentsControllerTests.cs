using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Api.Contracts.Residents;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-23/US-24: Tenant Profile Directory + Zero-Token Welcome & Provisioning --
/// ResidentProfile + one-to-many EmergencyContact, nested under a Property, plus login
/// provisioning (ApplicationUser/TenantMembership) for any resident with an email. Same
/// in-memory SQLite pattern as PropertiesControllerTests, extended with a minimal real
/// Identity DI stack (AddIdentityCore against the SAME SQLite connection) so
/// UserManager/RoleManager work for real rather than needing to be mocked -- this codebase
/// has no mocking library, and AuthController itself is only ever tested this way via full
/// integration tests, so building real Identity plumbing here (rather than skipping
/// provisioning coverage entirely) is the faithful choice.</summary>
public class ResidentsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public ResidentsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ResidentsController Controller, Guid PropertyId, UserManager<ApplicationUser> UserManager, FakeEmailSender EmailSender)
        CreateController(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var hardDeleteOverride = new HardDeleteOverride();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, hardDeleteOverride))
            .Options;
        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<Ten21DbContext>()
            .AddDefaultTokenProviders();
        var provider = services.BuildServiceProvider();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!roleManager.RoleExistsAsync(RoleNames.Tenant).GetAwaiter().GetResult())
        {
            roleManager.CreateAsync(new ApplicationRole(RoleNames.Tenant)).GetAwaiter().GetResult();
        }

        var emailSender = new FakeEmailSender();

        var property = new Property
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Riverside Apartments",
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = "100 Main St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Occupied,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Properties.Add(property);
        db.SaveChanges();

        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var controller = new ResidentsController(db, _sanitizer, userManager, roleManager, emailSender, authorizationService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        return (db, controller, property.Id, userManager, emailSender);
    }

    private class FakeEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Subject, string HtmlBody)> SentEmails { get; } = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            SentEmails.Add((toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static UpsertResidentRequest NewRequest(
        OccupantType occupantType = OccupantType.Primary,
        string? email = "resident@example.com",
        IReadOnlyList<EmergencyContactRequest>? emergencyContacts = null) => new(
        occupantType,
        FirstName: "Jamie",
        LastName: "Rivera",
        Email: email,
        PhoneNumber: "555-0100",
        ForwardingAddress: null,
        NoticeGivenDate: null,
        ShowInDirectory: false,
        EmergencyContacts: emergencyContacts ?? []);

    [Fact]
    public async Task CreateResident_PersistsAResidentProfile_ScopedToTheProperty()
    {
        var (db, controller, propertyId, _, _) = CreateController(Guid.NewGuid());

        // No email -- this test is about the profile/property-scoping behavior, not
        // provisioning (see the dedicated CreateResident_WithEmail_* tests below for that).
        var result = await controller.CreateResident(propertyId, NewRequest(email: null), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<ResidentResponse>(created.Value);
        Assert.Equal(propertyId, response.PropertyId);
        Assert.Equal(OccupantType.Primary, response.OccupantType);
        Assert.Null(response.UserId);
        Assert.Equal(1, await db.ResidentProfiles.CountAsync());
    }

    [Fact]
    public async Task CreateResident_WithEmergencyContacts_PersistsAllOfThem()
    {
        var (_, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var contacts = new List<EmergencyContactRequest>
        {
            new("Alex Rivera", "555-0101", "Spouse"),
            new("Sam Rivera", "555-0102", "Sibling"),
        };

        var result = await controller.CreateResident(propertyId, NewRequest(emergencyContacts: contacts), CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(2, response.EmergencyContacts.Count);
        Assert.Contains(response.EmergencyContacts, c => c.Name == "Alex Rivera" && c.Relationship == "Spouse");
    }

    [Fact]
    public async Task CreateResident_SecondaryOccupant_IsAllowedAndDistinctFromPrimary()
    {
        var (db, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        await controller.CreateResident(propertyId, NewRequest(OccupantType.Primary, email: "primary@example.com"), CancellationToken.None);

        await controller.CreateResident(propertyId, NewRequest(OccupantType.Secondary, email: "secondary@example.com"), CancellationToken.None);

        var residents = await db.ResidentProfiles.ToListAsync();
        Assert.Equal(2, residents.Count);
        Assert.Contains(residents, r => r.OccupantType == OccupantType.Primary);
        Assert.Contains(residents, r => r.OccupantType == OccupantType.Secondary);
    }

    [Fact]
    public async Task CreateResident_StripsHtmlFromStringFields()
    {
        var (_, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { FirstName = "<script>alert(1)</script>Jamie" };

        var result = await controller.CreateResident(propertyId, request, CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.DoesNotContain('<', response.FirstName);
        Assert.Contains("Jamie", response.FirstName);
    }

    [Fact]
    public async Task CreateResident_ThrowsValidationException_WhenFirstNameIsMissing()
    {
        var (_, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { FirstName = "" };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateResident(propertyId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateResident_ThrowsValidationException_WhenEmergencyContactPhoneIsMissing()
    {
        var (_, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var request = NewRequest(emergencyContacts: [new EmergencyContactRequest("Alex Rivera", "", "Spouse")]);

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateResident(propertyId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateResident_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (_, controller, _, _, _) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controller.CreateResident(Guid.NewGuid(), NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task GetResidents_ReturnsOnlyResidentsOfThatProperty()
    {
        var (db, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        await controller.CreateResident(propertyId, NewRequest(), CancellationToken.None);

        var otherProperty = new Property
        {
            Id = Guid.NewGuid(),
            TenantId = db.Properties.First().TenantId,
            Name = "Other Property",
            PropertyType = PropertyType.SingleFamily,
            StreetAddress1 = "200 Oak St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Vacant,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Properties.Add(otherProperty);
        await db.SaveChangesAsync();
        await controller.CreateResident(otherProperty.Id, NewRequest(email: "other@example.com"), CancellationToken.None);

        var result = await controller.GetResidents(propertyId, CancellationToken.None);

        var residents = Assert.IsAssignableFrom<IEnumerable<ResidentResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var single = Assert.Single(residents);
        Assert.Equal(propertyId, single.PropertyId);
    }

    [Fact]
    public async Task UpdateResident_ReplacesEmergencyContactsEntirely()
    {
        var (_, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var created = await controller.CreateResident(
            propertyId,
            NewRequest(emergencyContacts: [new EmergencyContactRequest("Alex Rivera", "555-0101", "Spouse")]),
            CancellationToken.None);
        var residentId = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var updateRequest = NewRequest(emergencyContacts: [new EmergencyContactRequest("Jordan Lee", "555-0199", "Friend")]);
        var updateResult = await controller.UpdateResident(propertyId, residentId, updateRequest, CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<OkObjectResult>(updateResult).Value);
        var contact = Assert.Single(response.EmergencyContacts);
        Assert.Equal("Jordan Lee", contact.Name);
    }

    [Fact]
    public async Task UpdateResident_ThrowsNotFound_WhenResidentBelongsToADifferentProperty()
    {
        var (db, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var created = await controller.CreateResident(propertyId, NewRequest(), CancellationToken.None);
        var residentId = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var otherPropertyId = Guid.NewGuid();
        db.Properties.Add(new Property
        {
            Id = otherPropertyId,
            TenantId = db.Properties.First().TenantId,
            Name = "Other Property",
            PropertyType = PropertyType.SingleFamily,
            StreetAddress1 = "200 Oak St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Vacant,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => controller.UpdateResident(otherPropertyId, residentId, NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteResident_SoftDeletes_AndExcludesFromSubsequentQueries()
    {
        var (db, controller, propertyId, _, _) = CreateController(Guid.NewGuid());
        var created = await controller.CreateResident(propertyId, NewRequest(), CancellationToken.None);
        var residentId = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await controller.DeleteResident(propertyId, residentId, CancellationToken.None);

        Assert.Empty(await db.ResidentProfiles.ToListAsync());
        var stillInDatabase = await db.ResidentProfiles.IgnoreQueryFilters().SingleAsync(r => r.Id == residentId);
        Assert.True(stillInDatabase.IsDeleted);
    }

    [Fact]
    public async Task GetResident_ThrowsNotFound_ForAnotherTenantsResident()
    {
        var (_, controllerA, propertyIdA, _, _) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateResident(propertyIdA, NewRequest(), CancellationToken.None);
        var residentId = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB, _, _, _) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controllerB.GetResident(propertyIdA, residentId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateResident_WithEmail_ProvisionsALogin_WithMustChangePasswordTrue()
    {
        var (db, controller, propertyId, userManager, emailSender) = CreateController(Guid.NewGuid());

        var result = await controller.CreateResident(propertyId, NewRequest(email: "jamie@example.com"), CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.NotNull(response.UserId);

        var provisionedUser = await userManager.FindByEmailAsync("jamie@example.com");
        Assert.NotNull(provisionedUser);
        Assert.True(provisionedUser!.MustChangePassword);
        Assert.Equal(response.UserId, provisionedUser.Id);

        var membership = await db.TenantMemberships.IgnoreQueryFilters().SingleAsync(m => m.UserId == provisionedUser.Id);
        Assert.True(membership.IsPrimary);

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Equal("jamie@example.com", sent.ToEmail);
        Assert.Contains("app.ten21.io/login", sent.HtmlBody);
    }

    [Fact]
    public async Task CreateResident_SecondaryOccupantWithEmail_AlsoProvisionsALogin()
    {
        var (_, controller, propertyId, userManager, _) = CreateController(Guid.NewGuid());

        var result = await controller.CreateResident(
            propertyId, NewRequest(OccupantType.Secondary, email: "secondary@example.com"), CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.NotNull(response.UserId);
        Assert.NotNull(await userManager.FindByEmailAsync("secondary@example.com"));
    }

    [Fact]
    public async Task CreateResident_WithNoEmail_DoesNotProvisionALogin()
    {
        var (_, controller, propertyId, _, emailSender) = CreateController(Guid.NewGuid());

        var result = await controller.CreateResident(propertyId, NewRequest(email: null), CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Null(response.UserId);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public async Task CreateResident_EmailAlreadyBelongsToAUser_LinksExistingAccount_WithoutResettingPassword()
    {
        var (db, controller, propertyId, userManager, emailSender) = CreateController(Guid.NewGuid());

        // Simulate a resident who already has an account -- e.g. from a different Property
        // Manager's tenant (see CreateResidentsController's ProvisionResidentLoginAsync doc
        // comment for the cross-PM reasoning).
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "existing@example.com",
            Email = "existing@example.com",
            FirstName = "Existing",
            LastName = "User",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await userManager.CreateAsync(existingUser, "Original-Passw0rd!1");

        var result = await controller.CreateResident(propertyId, NewRequest(email: "existing@example.com"), CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(existingUser.Id, response.UserId);

        var reloadedUser = await userManager.FindByIdAsync(existingUser.Id.ToString());
        Assert.False(reloadedUser!.MustChangePassword); // never flipped -- they keep their existing password
        Assert.True(await userManager.CheckPasswordAsync(reloadedUser, "Original-Passw0rd!1"));

        var membership = await db.TenantMemberships.IgnoreQueryFilters().SingleAsync(m => m.UserId == existingUser.Id);
        Assert.True(membership.IsPrimary); // their first (only) membership

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Contains("existing account", sent.HtmlBody);
    }
}
