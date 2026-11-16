using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

public sealed class NotFoundOptions
{
    public int Limit { get; set; } = 20;
    public int WindowMinutes { get; set; } = 10;
}

/// <summary>
/// Stops bill id guessing: past the limit of not-found lookups in one window, every bill request
/// from that IP gets 429, found or not, so the answer reveals nothing. Counted per salted IP hash.
/// </summary>
public sealed class NotFoundGuard(ILimitStore limits, PrivacyHash hash, TimeProvider clock, IOptions<NotFoundOptions> options)
{
    public async Task<bool> IsBlockedAsync(HttpContext http) =>
        await limits.GetCountAsync(await CounterAsync(http), http.RequestAborted) >= options.Value.Limit;

    public async Task RecordAsync(HttpContext http) =>
        await limits.IncrementAsync(await CounterAsync(http), http.RequestAborted);

    private async Task<string> CounterAsync(HttpContext http)
    {
        var now = clock.GetUtcNow();
        var window = (now.Hour * 60 + now.Minute) / options.Value.WindowMinutes;
        return $"{now:yyyy-MM-dd}/{await hash.OfClientAsync(http)}-notfound-{window}";
    }
}
