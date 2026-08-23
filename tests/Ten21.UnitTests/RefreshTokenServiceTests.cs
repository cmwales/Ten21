using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Identity.Services;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Covers RefreshTokenService.RevokeAndReissueForTenantAsync -- the fix for the switch-context
/// bug flagged in OrganizationController: without it, a refresh token stays pinned to the
/// tenant it was originally issued under, so a user who switches into a different tenant gets
/// silently bounced back to their primary tenant the moment their access token expires and the
/// frontend refreshes. RefreshToken isn't ITenantScopedEntity (see its class comment), so no
/// TenantContext/query-filter setup is needed here, same as RefreshTokenService itself.
/// </summary>
public class RefreshTokenServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Ten21DbContext _dbContext;
    private readonly RefreshTokenService _sut;

    public RefreshTokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new Ten21DbContext(options, new TenantContext());
        _dbContext.Database.EnsureCreated();
        _sut = new RefreshTokenService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // RefreshToken.UserId carries a real FK to ApplicationUser (RefreshTokenConfiguration),
    // so every test needs an actual row there, not just a random Guid.
    private async Task<Guid> SeedUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{Guid.NewGuid()}@example.com",
            Email = $"{Guid.NewGuid()}@example.com",
            EmailConfirmed = true,
            FirstName = "Test",
            LastName = "User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task SwitchThenRefresh_ReturnsTargetTenant_NotOriginalTenant()
    {
        var userId = await SeedUserAsync();
        var primaryTenantId = Guid.NewGuid();
        var switchedTenantId = Guid.NewGuid();

        // Simulates login: refresh token issued for the user's primary tenant.
        var loginRawToken = await _sut.IssueAsync(userId, primaryTenantId, "127.0.0.1", default);

        // Simulates OrganizationController.SwitchContext calling this after minting a new
        // access token for switchedTenantId.
        var switchedRawToken = await _sut.RevokeAndReissueForTenantAsync(
            userId, switchedTenantId, loginRawToken, "127.0.0.1", default);

        // Simulates AuthController.RefreshToken firing after the switched-context access
        // token expires: it rotates whatever raw token is in the cookie and rebuilds the
        // access token from rotation.TenantId.
        var rotation = await _sut.ValidateAndRotateAsync(switchedRawToken, "127.0.0.1", default);

        Assert.Equal(switchedTenantId, rotation.TenantId);
        Assert.NotEqual(primaryTenantId, rotation.TenantId);
    }

    [Fact]
    public async Task RevokeAndReissueForTenant_RevokesAndChainTracksTheOldToken()
    {
        var userId = await SeedUserAsync();
        var primaryTenantId = Guid.NewGuid();
        var switchedTenantId = Guid.NewGuid();

        var loginRawToken = await _sut.IssueAsync(userId, primaryTenantId, "127.0.0.1", default);
        var switchedRawToken = await _sut.RevokeAndReissueForTenantAsync(
            userId, switchedTenantId, loginRawToken, "127.0.0.1", default);

        var oldTokenHash = RefreshTokenHasher.Hash(loginRawToken);
        var oldToken = await _dbContext.RefreshTokens.SingleAsync(rt => rt.TokenHash == oldTokenHash);

        var newTokenHash = RefreshTokenHasher.Hash(switchedRawToken);
        var newToken = await _dbContext.RefreshTokens.SingleAsync(rt => rt.TokenHash == newTokenHash);

        Assert.NotNull(oldToken.RevokedAt);
        Assert.Equal(newToken.Id, oldToken.ReplacedByTokenId);
        Assert.Equal(switchedTenantId, newToken.TenantId);
    }

    [Fact]
    public async Task RevokeAndReissueForTenant_StillIssuesANewToken_WhenOldTokenIsMissing()
    {
        var userId = await SeedUserAsync();
        var switchedTenantId = Guid.NewGuid();

        var switchedRawToken = await _sut.RevokeAndReissueForTenantAsync(
            userId, switchedTenantId, oldRawToken: null, "127.0.0.1", default);

        var rotation = await _sut.ValidateAndRotateAsync(switchedRawToken, "127.0.0.1", default);
        Assert.Equal(switchedTenantId, rotation.TenantId);
    }
}
