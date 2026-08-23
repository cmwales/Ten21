using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Ten21.Infrastructure.RateLimiting;

/// <summary>
/// IP-partitioned sliding-window rate limiter for /api/auth/* routes (SECURITY.docx §1:
/// "IP-based sliding window rate limiting, max 5 requests per minute").
///
/// Deliberately NOT the simpler RateLimiterOptions.AddSlidingWindowLimiter(name, configure)
/// overload -- that overload creates ONE GLOBAL limiter shared across every caller, not a
/// separate bucket per client IP. One busy legitimate client (or one attacker) would
/// exhaust the shared budget and lock out everyone else. RateLimitPartition with an
/// IP-based partition key gives each client their own independent 5-req/min allowance,
/// which is what "per client IP" in the acceptance criteria actually requires.
/// </summary>
public static class AuthRateLimiterPolicy
{
    public const string PolicyName = "auth";

    public static RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0, // reject immediately over the limit rather than queueing -- an
                             // auth endpoint shouldn't make a brute-force attempt wait and
                             // retry automatically, it should fail fast.
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }
}
