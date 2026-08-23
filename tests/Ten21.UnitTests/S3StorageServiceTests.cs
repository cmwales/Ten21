using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Ten21.Infrastructure.Storage;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Presigned URL generation is pure local HMAC signing -- the AWS SDK never contacts S3/R2
/// to produce one, which is what makes this genuinely unit-testable with fake credentials.
/// </summary>
public class S3StorageServiceTests
{
    private static S3StorageService CreateService()
    {
        var s3Client = new AmazonS3Client(
            "fake-access-key",
            "fake-secret-key",
            new AmazonS3Config { ServiceURL = "https://fake-r2-endpoint.test", ForcePathStyle = true });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ObjectStorage:BucketName"] = "ten21-test-bucket",
            })
            .Build();

        return new S3StorageService(s3Client, configuration);
    }

    [Fact]
    public void CreatePresignedUpload_ObjectKeyFollowsTheDocumentedPathShape()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var result = service.CreatePresignedUpload(tenantId, "LeaseDocs", entityId, "photo.jpg", "image/jpeg");

        // {TenantId}/{Category}/{EntityId}/{Guid}.ext, per US-06's acceptance criteria.
        var segments = result.ObjectKey.Split('/');
        Assert.Equal(4, segments.Length);
        Assert.Equal(tenantId.ToString(), segments[0]);
        Assert.Equal("LeaseDocs", segments[1]);
        Assert.Equal(entityId.ToString(), segments[2]);
        Assert.EndsWith(".jpg", segments[3]);
    }

    [Fact]
    public void CreatePresignedUpload_SanitizesCategoryInTheObjectKey()
    {
        var service = CreateService();

        var result = service.CreatePresignedUpload(
            Guid.NewGuid(), "../../other-tenant", Guid.NewGuid(), "file.pdf", "application/pdf");

        Assert.DoesNotContain("..", result.ObjectKey);
    }

    [Fact]
    public void CreatePresignedUpload_ExpiresApproximatelyFifteenMinutesFromNow()
    {
        var service = CreateService();
        var before = DateTimeOffset.UtcNow;

        var result = service.CreatePresignedUpload(Guid.NewGuid(), "docs", Guid.NewGuid(), "f.pdf", "application/pdf");

        var difference = (result.ExpiresAtUtc - before.AddMinutes(15)).Duration();
        Assert.True(difference < TimeSpan.FromSeconds(5), $"Expected ~15 minutes, was off by {difference}");
    }

    [Fact]
    public void CreatePresignedUpload_ProducesAWellFormedUrl()
    {
        var service = CreateService();

        var result = service.CreatePresignedUpload(Guid.NewGuid(), "docs", Guid.NewGuid(), "f.pdf", "application/pdf");

        Assert.StartsWith("https://", result.UploadUrl);
        Assert.Contains("ten21-test-bucket", result.UploadUrl);
    }

    [Fact]
    public void Constructor_ThrowsIfBucketNameMissing()
    {
        var s3Client = new AmazonS3Client("fake", "fake", new AmazonS3Config { ServiceURL = "https://fake.test" });
        var configuration = new ConfigurationBuilder().Build(); // no BucketName set

        Assert.Throws<InvalidOperationException>(() => new S3StorageService(s3Client, configuration));
    }
}
