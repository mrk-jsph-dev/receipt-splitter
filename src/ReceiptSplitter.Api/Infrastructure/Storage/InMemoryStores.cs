using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Infrastructure.Storage;

/// <summary>For tests and local runs without Azurite. Stores JSON so readers never share objects.</summary>
public sealed class InMemoryBillStore : IBillStore
{
    private readonly Dictionary<string, (string Json, string ETag)> _bills = new();
    private readonly Lock _lock = new();

    public Task<StoredBill?> GetAsync(string id, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_bills.TryGetValue(id, out var entry)
                ? new StoredBill(JsonSerializer.Deserialize<BillDocument>(entry.Json, ApiJson.Options)!, entry.ETag)
                : null);
        }
    }

    public Task<bool> CreateAsync(BillDocument bill, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult(_bills.TryAdd(bill.Id, (Serialize(bill), NewETag())));
    }

    public Task<bool> ReplaceAsync(BillDocument bill, string etag, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_bills.TryGetValue(bill.Id, out var entry) || entry.ETag != etag)
                return Task.FromResult(false);
            _bills[bill.Id] = (Serialize(bill), NewETag());
            return Task.FromResult(true);
        }
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        lock (_lock)
            _bills.Remove(id);
        return Task.CompletedTask;
    }

    private static string Serialize(BillDocument bill) => JsonSerializer.Serialize(bill, ApiJson.Options);

    private static string NewETag() => Guid.NewGuid().ToString("N");
}

public sealed class InMemoryLimitStore : ILimitStore
{
    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, long> _counts = new();

    public Task<byte[]> GetSaltAsync(CancellationToken ct) => Task.FromResult(_salt);

    public Task<long> GetCountAsync(string counter, CancellationToken ct) =>
        Task.FromResult(_counts.GetValueOrDefault(counter));

    public Task<long> IncrementAsync(string counter, CancellationToken ct) =>
        Task.FromResult(_counts.AddOrUpdate(counter, 1, (_, n) => n + 1));
}
