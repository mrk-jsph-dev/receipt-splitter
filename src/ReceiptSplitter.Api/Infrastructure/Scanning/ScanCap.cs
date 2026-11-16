using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Infrastructure.Scanning;

/// <summary>Scan limits from the "Scans" config section. The free tier reads a fixed number of pages a month.</summary>
public sealed class ScanOptions
{
    /// <summary>Scans per day across everyone. 15 a day stays inside the F0 monthly pages [VERIFY F0 monthly allowance].</summary>
    public int DailyCap { get; set; } = 15;

    public int PerIpPerHour { get; set; } = 3;

    /// <summary>F0 refuses documents over 4 MB (Document Intelligence service limits, v4.0).</summary>
    public int MaxBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Local runs without Azure: every photo reads as the sample receipt. Refused in production.</summary>
    public bool UseSample { get; set; }
}

/// <summary>The daily scan cap, counted in the same store as the new-bill cap. Every file counts once.</summary>
public sealed class ScanCap(ILimitStore limits, TimeProvider clock, IOptions<ScanOptions> options)
{
    public async Task<int> RemainingAsync(CancellationToken ct) =>
        (int)Math.Max(0, options.Value.DailyCap - await limits.GetCountAsync(Counter(), ct));

    public Task<long> RecordAsync(CancellationToken ct) => limits.IncrementAsync(Counter(), ct);

    private string Counter() => $"{clock.GetUtcNow():yyyy-MM-dd}/scans";
}
