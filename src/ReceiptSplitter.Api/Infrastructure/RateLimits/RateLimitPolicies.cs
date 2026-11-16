using System.Threading.RateLimiting;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

/// <summary>Per-IP limits from the "RateLimits" config section. Partitions live in memory only and are never logged.</summary>
public sealed class RateLimitOptions
{
    public int CreatePerHour { get; set; } = 10;
    public int WritesPerMinute { get; set; } = 60;
}

public static class RateLimitPolicies
{
    public const string Create = "create";
    public const string Write = "write";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, ct) =>
                new ValueTask(ApiResults.TooMany("rate").ExecuteAsync(context.HttpContext));

            limiter.AddPolicy(Create, http => PerClient(http, options.CreatePerHour, TimeSpan.FromHours(1)));
            limiter.AddPolicy(Write, http => PerClient(http, options.WritesPerMinute, TimeSpan.FromMinutes(1)));
        });
    }

    private static RateLimitPartition<string> PerClient(HttpContext http, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 });
}
