using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

public sealed class PinOptions
{
    /// <summary>Wrong tries in one window before the PIN locks for the rest of it.</summary>
    public int TriesPerWindow { get; set; } = 5;
    public int WindowMinutes { get; set; } = 15;

    /// <summary>Wrong tries in one UTC day, so a 4-digit PIN can't be worked through over the bill's 30 days.</summary>
    public int TriesPerDay { get; set; } = 20;
}

/// <summary>
/// Wrong PIN tries, counted per bill (as a salted hash) rather than per IP, so guessing from many
/// networks doesn't help. Counters live under limits/ and expire with it.
/// </summary>
public sealed class PinGuard(ILimitStore limits, PrivacyHash hash, TimeProvider clock, IOptions<PinOptions> options)
{
    public sealed record State(int TriesLeft, TimeSpan? LockedFor);

    public async Task<State> CheckAsync(string billId, CancellationToken ct)
    {
        var (window, day, windowEnd) = await CountersAsync(billId);
        var o = options.Value;
        var inWindow = await limits.GetCountAsync(window, ct);
        var inDay = await limits.GetCountAsync(day, ct);
        var now = clock.GetUtcNow();
        if (inDay >= o.TriesPerDay)
            return new State(0, now.Date.AddDays(1) - now.UtcDateTime);
        if (inWindow >= o.TriesPerWindow)
            return new State(0, windowEnd - now);
        return new State((int)Math.Min(o.TriesPerWindow - inWindow, o.TriesPerDay - inDay), null);
    }

    /// <summary>Counts one wrong try and returns how many are left before the lock.</summary>
    public async Task<int> RecordWrongAsync(string billId, CancellationToken ct)
    {
        var (window, day, _) = await CountersAsync(billId);
        var o = options.Value;
        var inWindow = await limits.IncrementAsync(window, ct);
        var inDay = await limits.IncrementAsync(day, ct);
        return (int)Math.Max(0, Math.Min(o.TriesPerWindow - inWindow, o.TriesPerDay - inDay));
    }

    private async Task<(string Window, string Day, DateTimeOffset WindowEnd)> CountersAsync(string billId)
    {
        var now = clock.GetUtcNow();
        var minutes = options.Value.WindowMinutes;
        var window = (now.Hour * 60 + now.Minute) / minutes;
        var bill = await hash.OfAsync(billId);
        var end = new DateTimeOffset(now.Date, TimeSpan.Zero).AddMinutes((window + 1) * minutes);
        return ($"{now:yyyy-MM-dd}/{bill}-pin-{window}", $"{now:yyyy-MM-dd}/{bill}-pin-day", end);
    }
}
