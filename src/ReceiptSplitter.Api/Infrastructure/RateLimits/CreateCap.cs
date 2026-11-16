using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

/// <summary>
/// A ceiling on new bills per UTC day across every client, on top of the per-IP limit. Kept in the
/// limit store so scale-to-zero doesn't reset it. Only bills that were actually created count.
/// </summary>
public sealed class CreateCap(ILimitStore limits, TimeProvider clock, IOptions<RateLimitOptions> options)
{
    public async Task<bool> IsReachedAsync(CancellationToken ct) =>
        await limits.GetCountAsync(Counter(), ct) >= options.Value.CreatePerDay;

    public Task RecordAsync(CancellationToken ct) => limits.IncrementAsync(Counter(), ct);

    private string Counter() => $"{clock.GetUtcNow():yyyy-MM-dd}/bills-created";
}
