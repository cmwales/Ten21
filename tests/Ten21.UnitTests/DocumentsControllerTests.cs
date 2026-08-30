using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Business.Documents;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Covers the US-09 exception-taxonomy retrofit: PresignUpload's two validation checks used
/// to `return BadRequest(new { message = ... })` directly, which bypassed GlobalExceptionHandler
/// (see GlobalExceptionHandlerTests for the RFC 7807 pipeline itself) and produced a bare
/// { message } body instead of a ProblemDetails one. They now throw ValidationException with
/// a populated field-level Errors dictionary, same as every other validation failure in the API.
///
/// Also covers the Audit Refinement Sprint's EntityId-ownership check (AnyTenantScopedRecordExistsAsync).
/// </summary>
public class DocumentsControllerTests : IDisposable
{
    private class NeverCalledStorageService : IS3StorageService
    {
        public PresignedUpload CreatePresignedUpload(
            Guid tenantId, string category, Guid entityId, string fileName, string contentType)
            => throw new InvalidOperationException(
                "CreatePresignedUpload should not be reached when request validation fails.");
    }

    private class FakeStorageService : IS3StorageService
    {
        public Guid? LastEntityId { get; private set; }

        public PresignedUpload CreatePresignedUpload(
            Guid tenantId, string category, Guid entityId, string fileName, string contentType)
        {
            LastEntityId = entityId;
            return new PresignedUpload(
                "https://example-bucket.s3.amazonaws.com/fake-upload-url",
                $"{tenantId}/{category}/{entityId}/{Guid.NewGuid()}.pdf",
                DateTimeOffset.UtcNow.AddMinutes(15));
        }
    }

    private readonly SqliteConnection _connection;
    private readonly Ten21DbContext _dbContext;
    private readonly TenantContext _tenantContext;
    private readonly DocumentsController _sut;

    public DocumentsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .Options;

        _tenantContext = new TenantContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenantContext.SetTenant(tenantId);

        _dbContext = new Ten21DbContext(options, _tenantContext);
        _dbContext.Database.EnsureCreated();

        _sut = new DocumentsController(new DocumentService(new NeverCalledStorageService(), _dbContext, _tenantContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("user_id", userId.ToString())], "TestAuth")),
                },
            },
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task PresignUpload_ThrowsValidationException_ForDisallowedContentType()
    {
        var request = new PresignUploadRequest("lease", Guid.NewGuid(), "malware.exe", "application/x-msdownload", 1024);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.PresignUpload(request, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("ContentType"));
    }

    [Fact]
    public async Task PresignUpload_ThrowsValidationException_ForOversizedFile()
    {
        var request = new PresignUploadRequest(
            "lease", Guid.NewGuid(), "big.pdf", "application/pdf", DocumentUploadPolicy.MaxByteSize + 1);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.PresignUpload(request, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("ByteSize"));
    }

    [Fact]
    public async Task PresignUpload_ThrowsNotFound_WhenEntityDoesNotBelongToCallersTenant()
    {
        var request = new PresignUploadRequest("lease", Guid.NewGuid(), "lease.pdf", "application/pdf", 1024);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PresignUpload(request, CancellationToken.None));
    }

    [Fact]
    public async Task PresignUpload_Succeeds_WhenEntityIdIsAPropertyInTheCallersOwnTenant()
    {
        var property = new Property
        {
            Id = Guid.NewGuid(),
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
        _dbContext.Properties.Add(property);
        await _dbContext.SaveChangesAsync();

        var storageService = new FakeStorageService();
        var sut = new DocumentsController(new DocumentService(storageService, _dbContext, _tenantContext))
        {
            ControllerContext = _sut.ControllerContext,
        };

        var request = new PresignUploadRequest("lease", property.Id, "lease.pdf", "application/pdf", 1024);
        var result = await sut.PresignUpload(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(property.Id, storageService.LastEntityId);
    }
}
