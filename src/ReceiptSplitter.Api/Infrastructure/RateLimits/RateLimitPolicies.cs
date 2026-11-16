using System.Threading.RateLimiting;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

/// <summary>Per-IP limits from the "RateLimits" config section. Partitions live in memory only and are never logged.</summary>
public sealed class RateLimitOptions
{
    public int CreatePerHour { get; set; } = 10;
    public int WritesPerMinute { get; set; } = 60;

    /// <summary>Bill reads. Polling every 10 seconds is 6 a minute, so this leaves room for several tabs.</summary>
    public int ReadsPerMinute { get; set; } = 120;

    /// <summary>New bills across all clients, so many IPs together still can't fill storage. [VERIFY] the number.</summary>
    public int CreatePerDay { get; set; } = 500;
}

public static class RateLimitPolicies
{
    public const string Create = "create";
    public const string Write = "write";
    public const string Read = "read";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.Configure<RateLimitOptions>(config.GetSection("RateLimits"));
        services.AddSingleton<CreateCap>();
        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, ct) =>
                new ValueTask(ApiResults.TooMany("rate").ExecuteAsync(context.HttpContext));

            limiter.AddPolicy(Create, http => PerClient(http, options.CreatePerHour, TimeSpan.FromHours(1)));
            limiter.AddPolicy(Write, http => PerClient(http, options.WritesPerMinute, TimeSpan.FromMinutes(1)));
            limiter.AddPolicy(Read, http => PerClient(http, options.ReadsPerMinute, TimeSpan.FromMinutes(1)));
        });
    }

    private static RateLimitPartition<string> PerClient(HttpContext http, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey.Of(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 });
}
