using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Documents;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
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

    private readonly SqliteConnection _connection;
    private readonly Ten21DbContext _dbContext;
    private readonly DocumentsController _sut;

    public DocumentsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .Options;

        var tenantContext = new TenantContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        tenantContext.SetTenant(tenantId);

        _dbContext = new Ten21DbContext(options, tenantContext);
        _dbContext.Database.EnsureCreated();

        _sut = new DocumentsController(new NeverCalledStorageService(), _dbContext, tenantContext)
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
}
