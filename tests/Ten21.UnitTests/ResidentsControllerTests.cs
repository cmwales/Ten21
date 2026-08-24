using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Residents;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-23: Tenant Profile Directory -- ResidentProfile + one-to-many
/// EmergencyContact, nested under a Property. Same in-memory SQLite pattern as
/// PropertiesControllerTests.</summary>
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

    private (Ten21DbContext Db, ResidentsController Controller, Guid PropertyId) CreateController(Guid tenantId)
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

        return (db, new ResidentsController(db, _sanitizer), property.Id);
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
        var (db, controller, propertyId) = CreateController(Guid.NewGuid());

        var result = await controller.CreateResident(propertyId, NewRequest(), CancellationToken.None);

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
        var (_, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (db, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (_, controller, propertyId) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { FirstName = "<script>alert(1)</script>Jamie" };

        var result = await controller.CreateResident(propertyId, request, CancellationToken.None);

        var response = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.DoesNotContain('<', response.FirstName);
        Assert.Contains("Jamie", response.FirstName);
    }

    [Fact]
    public async Task CreateResident_ThrowsValidationException_WhenFirstNameIsMissing()
    {
        var (_, controller, propertyId) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { FirstName = "" };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateResident(propertyId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateResident_ThrowsValidationException_WhenEmergencyContactPhoneIsMissing()
    {
        var (_, controller, propertyId) = CreateController(Guid.NewGuid());
        var request = NewRequest(emergencyContacts: [new EmergencyContactRequest("Alex Rivera", "", "Spouse")]);

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateResident(propertyId, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateResident_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (_, controller, _) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controller.CreateResident(Guid.NewGuid(), NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task GetResidents_ReturnsOnlyResidentsOfThatProperty()
    {
        var (db, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (_, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (db, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (db, controller, propertyId) = CreateController(Guid.NewGuid());
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
        var (_, controllerA, propertyIdA) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateResident(propertyIdA, NewRequest(), CancellationToken.None);
        var residentId = Assert.IsType<ResidentResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB, _) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controllerB.GetResident(propertyIdA, residentId, CancellationToken.None));
    }
}
