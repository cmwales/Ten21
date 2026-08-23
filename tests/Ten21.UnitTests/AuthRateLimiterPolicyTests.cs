using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Ten21.Infrastructure.RateLimiting;
using Xunit;

namespace Ten21.UnitTests;

public class AuthRateLimiterPolicyTests
{
    private static DefaultHttpContext ContextFromIp(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public async Task AllowsExactlyFiveRequests_ThenRejectsTheSixth()
    {
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimiterPolicy.GetPartition);
        var context = ContextFromIp("203.0.113.5");

        for (var i = 0; i < 5; i++)
        {
            using var lease = await limiter.AcquireAsync(context);
            Assert.True(lease.IsAcquired, $"Request {i + 1} should have been permitted.");
        }

        using var sixthLease = await limiter.AcquireAsync(context);
        Assert.False(sixthLease.IsAcquired, "The 6th request within the window should be rejected.");
    }

    [Fact]
    public async Task DifferentIpAddresses_GetIndependentBudgets()
    {
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimiterPolicy.GetPartition);
        var contextA = ContextFromIp("203.0.113.5");
        var contextB = ContextFromIp("203.0.113.9");

        for (var i = 0; i < 5; i++)
        {
            using var lease = await limiter.AcquireAsync(contextA);
            Assert.True(lease.IsAcquired);
        }

        // A is now exhausted -- B, a different IP, must still have its own full budget.
        using var leaseB = await limiter.AcquireAsync(contextB);
        Assert.True(leaseB.IsAcquired);
    }

    [Fact]
    public async Task MissingRemoteIpAddress_StillGetsARateLimitedBudget()
    {
        // Falls back to a shared "unknown" partition rather than throwing or silently
        // allowing unlimited requests -- worth a test since this is exactly the kind of
        // edge case (e.g. certain test/proxy setups) that's easy to leave unverified.
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimiterPolicy.GetPartition);
        var context = new DefaultHttpContext(); // RemoteIpAddress left null

        for (var i = 0; i < 5; i++)
        {
            using var lease = await limiter.AcquireAsync(context);
            Assert.True(lease.IsAcquired);
        }

        using var sixthLease = await limiter.AcquireAsync(context);
        Assert.False(sixthLease.IsAcquired);
    }
}
