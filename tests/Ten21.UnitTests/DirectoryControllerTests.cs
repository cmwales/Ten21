using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Controllers;
using Ten21.Business.Directory;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-25: Tenant Access &amp; Directory Privacy -- the dual-consent community
/// directory (Property.AllowTenantDirectory AND ResidentProfile.ShowInDirectory). No
/// propertyId route parameter exists to tamper with (BOLA-safe by construction): the
/// caller's own occupancy, resolved from their user_id claim, is what scopes every
/// query.</summary>
public class DirectoryControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DirectoryControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, DirectoryController Controller) CreateController(Guid tenantId, Guid callerUserId)
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

        var controller = new DirectoryController(new DirectoryService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("user_id", callerUserId.ToString())], "TestAuth")),
                },
            },
        };

        return (db, controller);
    }

    private static Property NewProperty(Guid tenantId, string streetAddress1, bool allowTenantDirectory, string? unitIdentifier = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "Riverside Apartments",
        PropertyType = PropertyType.MultiFamily,
        StreetAddress1 = streetAddress1,
        City = "Provo",
        State = "UT",
        PostalCode = "84601",
        Country = "USA",
        UnitIdentifier = unitIdentifier,
        OccupancyStatus = OccupancyStatus.Occupied,
        AllowTenantDirectory = allowTenantDirectory,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ResidentProfile NewResident(Guid tenantId, Guid propertyId, Guid? userId, bool showInDirectory, string firstName = "Jamie") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PropertyId = propertyId,
        UserId = userId,
        OccupantType = OccupantType.Primary,
        FirstName = firstName,
        LastName = "Rivera",
        ShowInDirectory = showInDirectory,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task GetDirectory_ReturnsSiblingResident_WhenBothPropertyAndResidentOptIn()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite A");
        var siblingProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite B");
        db.Properties.AddRange(callerProperty, siblingProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, siblingProperty.Id, Guid.NewGuid(), showInDirectory: true, firstName: "Sam"));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var entry = Assert.Single(entries);
        Assert.Equal("Sam", entry.FirstName);
        Assert.Equal("Suite B", entry.UnitIdentifier);
    }

    [Fact]
    public async Task GetDirectory_ExcludesSibling_WhenSiblingPropertyDoesNotAllowDirectory()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        var siblingProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: false);
        db.Properties.AddRange(callerProperty, siblingProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, siblingProperty.Id, Guid.NewGuid(), showInDirectory: true));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetDirectory_ExcludesResident_WhenResidentOptedOut()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        var siblingProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        db.Properties.AddRange(callerProperty, siblingProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, siblingProperty.Id, Guid.NewGuid(), showInDirectory: false));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetDirectory_ExcludesTheCallersOwnEntry()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        db.Properties.Add(callerProperty);
        db.ResidentProfiles.Add(NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetDirectory_ExcludesResidentsAtADifferentAddress()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        var unrelatedProperty = NewProperty(tenantId, "999 Other Ave", allowTenantDirectory: true);
        db.Properties.AddRange(callerProperty, unrelatedProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, unrelatedProperty.Id, Guid.NewGuid(), showInDirectory: true));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetDirectory_ReturnsEmpty_WhenCallerHasNoResidentProfile()
    {
        var (_, controller) = CreateController(Guid.NewGuid(), Guid.NewGuid());

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(entries);
    }

    /// <summary>Refinement Sprint (Directive 4): a workspace-wide EnableCommunityDirectory=false
    /// hard-blocks this endpoint regardless of the dual-consent opt-ins below it.</summary>
    [Fact]
    public async Task GetDirectory_ThrowsForbidden_WhenWorkspaceDirectoryIsDisabled()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite A");
        var siblingProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite B");
        db.Properties.AddRange(callerProperty, siblingProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, siblingProperty.Id, Guid.NewGuid(), showInDirectory: true, firstName: "Sam"));
        db.WorkspaceSettings.Add(new WorkspaceSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EnableCommunityDirectory = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => controller.GetDirectory(CancellationToken.None));
    }

    [Fact]
    public async Task GetDirectory_Allows_WhenNoWorkspaceSettingsRowExistsYet()
    {
        // No WorkspaceSettings row seeded at all -- the default (enabled) must apply, since
        // WorkspaceSettingsController lazily creates the row rather than seeding it upfront.
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, callerId);

        var callerProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite A");
        var siblingProperty = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite B");
        db.Properties.AddRange(callerProperty, siblingProperty);
        db.ResidentProfiles.AddRange(
            NewResident(tenantId, callerProperty.Id, callerId, showInDirectory: true),
            NewResident(tenantId, siblingProperty.Id, Guid.NewGuid(), showInDirectory: true, firstName: "Sam"));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectory(CancellationToken.None);

        var entries = Assert.IsAssignableFrom<IEnumerable<DirectoryEntryResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Single(entries);
    }

    [Fact]
    public async Task GetDirectoryAdmin_ReturnsEntry_WhenBothPropertyAndResidentOptIn()
    {
        var tenantId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, Guid.NewGuid());

        var property = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true, unitIdentifier: "Suite A");
        db.Properties.Add(property);
        var resident = NewResident(tenantId, property.Id, userId: null, showInDirectory: true);
        resident.Email = "jamie@example.com";
        resident.PhoneNumber = "555-0100";
        db.ResidentProfiles.Add(resident);
        await db.SaveChangesAsync();

        var result = await controller.GetDirectoryAdmin(CancellationToken.None);

        var response = Assert.IsType<DirectoryAdminResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.WorkspaceDirectoryEnabled);
        var entry = Assert.Single(response.Entries);
        Assert.Equal("Jamie", entry.FirstName);
        Assert.Equal("jamie@example.com", entry.Email);
        Assert.Equal("555-0100", entry.PhoneNumber);
        Assert.Equal("Suite A", entry.UnitIdentifier);
        Assert.Equal("100 Main St, Provo, UT 84601", entry.PropertyAddress);
    }

    [Fact]
    public async Task GetDirectoryAdmin_ExcludesEntry_WhenPropertyDoesNotAllowDirectory()
    {
        var tenantId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, Guid.NewGuid());

        var property = NewProperty(tenantId, "100 Main St", allowTenantDirectory: false);
        db.Properties.Add(property);
        db.ResidentProfiles.Add(NewResident(tenantId, property.Id, userId: null, showInDirectory: true));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectoryAdmin(CancellationToken.None);

        var response = Assert.IsType<DirectoryAdminResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(response.Entries);
    }

    [Fact]
    public async Task GetDirectoryAdmin_ExcludesEntry_WhenResidentOptedOut()
    {
        var tenantId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, Guid.NewGuid());

        var property = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        db.Properties.Add(property);
        db.ResidentProfiles.Add(NewResident(tenantId, property.Id, userId: null, showInDirectory: false));
        await db.SaveChangesAsync();

        var result = await controller.GetDirectoryAdmin(CancellationToken.None);

        var response = Assert.IsType<DirectoryAdminResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(response.Entries);
    }

    /// <summary>Unlike GetDirectory, GetDirectoryAdmin never throws when the workspace toggle
    /// is off -- a PM needs to see what WOULD show while deciding whether to enable it.</summary>
    [Fact]
    public async Task GetDirectoryAdmin_StillReturnsEntries_WhenWorkspaceDirectoryIsDisabled()
    {
        var tenantId = Guid.NewGuid();
        var (db, controller) = CreateController(tenantId, Guid.NewGuid());

        var property = NewProperty(tenantId, "100 Main St", allowTenantDirectory: true);
        db.Properties.Add(property);
        db.ResidentProfiles.Add(NewResident(tenantId, property.Id, userId: null, showInDirectory: true));
        db.WorkspaceSettings.Add(new WorkspaceSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EnableCommunityDirectory = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await controller.GetDirectoryAdmin(CancellationToken.None);

        var response = Assert.IsType<DirectoryAdminResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.WorkspaceDirectoryEnabled);
        Assert.Single(response.Entries);
    }
}
