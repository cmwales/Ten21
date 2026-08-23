using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Storage;

public static class ObjectStorageServiceCollectionExtensions
{
    public static IServiceCollection AddObjectStorage(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var accessKey = configuration["ObjectStorage:AccessKey"];
            var secretKey = configuration["ObjectStorage:SecretKey"];
            var serviceUrl = configuration["ObjectStorage:ServiceUrl"]; // set for R2; leave unset for real AWS S3

            var s3Config = new AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                s3Config.ServiceURL = serviceUrl;
                s3Config.ForcePathStyle = true; // required for R2 and most S3-compatible providers
            }
            else
            {
                // TODO: make configurable per deployment region once a real AWS account/
                // region is chosen -- US East 1 is a placeholder, not a decision.
                s3Config.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
            }

            // AmazonS3Client's constructor never makes a network call -- presigned URL
            // generation (S3StorageService) is pure local signing, so this is safe to
            // construct even with placeholder/empty local dev credentials. It just won't
            // produce a URL that actually works against a real bucket until real
            // credentials are set via `dotnet user-secrets`.
            return new AmazonS3Client(accessKey, secretKey, s3Config);
        });

        services.AddScoped<IS3StorageService, S3StorageService>();

        return services;
    }
}
