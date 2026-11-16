using System.Security.Cryptography;
using System.Text;
using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Infrastructure.Security;

/// <summary>
/// Salted hashes for logs and rate-limit counters, so neither holds a bill id or an IP address.
/// The salt lives in the limit store and rotates when the 2-day lifecycle rule deletes it.
/// </summary>
public sealed class PrivacyHash(ILimitStore limits)
{
    public async Task<string> OfAsync(string value, CancellationToken ct = default)
    {
        var salt = await limits.GetSaltAsync(ct);
        var hash = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    public Task<string> OfClientAsync(HttpContext http) =>
        OfAsync(ClientKey.Of(http), http.RequestAborted);
}
