using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Infrastructure.Storage;

/// <summary>Bills as bills/{id}.json. Every write is conditional on the blob's ETag.</summary>
public sealed class BlobBillStore(BlobServiceClient service) : IBillStore
{
    private readonly BlobContainerClient _container = service.GetBlobContainerClient("bills");
    private readonly Lazy<Task> _ensureContainer = new(() => service.GetBlobContainerClient("bills").CreateIfNotExistsAsync());

    public async Task<StoredBill?> GetAsync(string id, CancellationToken ct)
    {
        await _ensureContainer.Value;
        try
        {
            var result = await Blob(id).DownloadContentAsync(ct);
            var bill = result.Value.Content.ToObjectFromJson<BillDocument>(ApiJson.Options)!;
            return new StoredBill(bill, result.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public Task<bool> CreateAsync(BillDocument bill, CancellationToken ct) =>
        Upload(bill, new BlobRequestConditions { IfNoneMatch = ETag.All }, ct);

    public Task<bool> ReplaceAsync(BillDocument bill, string etag, CancellationToken ct) =>
        Upload(bill, new BlobRequestConditions { IfMatch = new ETag(etag) }, ct);

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        await _ensureContainer.Value;
        await Blob(id).DeleteIfExistsAsync(cancellationToken: ct);
    }

    private async Task<bool> Upload(BillDocument bill, BlobRequestConditions conditions, CancellationToken ct)
    {
        await _ensureContainer.Value;
        try
        {
            await Blob(bill.Id).UploadAsync(
                BinaryData.FromObjectAsJson(bill, ApiJson.Options),
                new BlobUploadOptions { Conditions = conditions, HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } },
                ct);
            return true;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 409 or 412)
        {
            return false;
        }
    }

    private BlobClient Blob(string id) => _container.GetBlobClient($"{id}.json");
}

/// <summary>
/// Salt and counters under limits/. The API runs as a single replica, so counts are cached in
/// memory and the blobs only carry them across restarts and scale-to-zero.
/// </summary>
public sealed class BlobLimitStore(BlobServiceClient service, TimeProvider clock) : ILimitStore
{
    private static readonly TimeSpan SaltCacheTime = TimeSpan.FromMinutes(10);

    private readonly BlobContainerClient _container = service.GetBlobContainerClient("limits");
    private readonly Lazy<Task> _ensureContainer = new(() => service.GetBlobContainerClient("limits").CreateIfNotExistsAsync());
    private readonly ConcurrentDictionary<string, long> _counts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private (byte[] Salt, DateTimeOffset ReadAt)? _salt;

    public async Task<byte[]> GetSaltAsync(CancellationToken ct)
    {
        if (_salt is { } cached && clock.GetUtcNow() - cached.ReadAt < SaltCacheTime)
            return cached.Salt;

        await _ensureContainer.Value;
        var blob = _container.GetBlobClient("salt.json");
        byte[] salt;
        try
        {
            salt = (await blob.DownloadContentAsync(ct)).Value.Content.ToObjectFromJson<byte[]>()!;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // The lifecycle rule deletes it after 2 days; a new one is made on first use.
            salt = RandomNumberGenerator.GetBytes(32);
            try
            {
                await blob.UploadAsync(BinaryData.FromObjectAsJson(salt),
                    new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, ct);
            }
            catch (RequestFailedException race) when (race.Status is 409 or 412)
            {
                salt = (await blob.DownloadContentAsync(ct)).Value.Content.ToObjectFromJson<byte[]>()!;
            }
        }

        _salt = (salt, clock.GetUtcNow());
        return salt;
    }

    public async Task<long> GetCountAsync(string counter, CancellationToken ct)
    {
        if (_counts.TryGetValue(counter, out var count))
            return count;
        var (stored, _) = await Read(counter, ct);
        return _counts.GetOrAdd(counter, stored);
    }

    public async Task<long> IncrementAsync(string counter, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var (stored, etag) = await Read(counter, ct);
                var next = Math.Max(stored, _counts.GetValueOrDefault(counter)) + 1;
                var conditions = etag is null
                    ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                    : new BlobRequestConditions { IfMatch = etag };
                try
                {
                    await _container.GetBlobClient($"{counter}.json")
                        .UploadAsync(BinaryData.FromObjectAsJson(next), new BlobUploadOptions { Conditions = conditions }, ct);
                    _counts[counter] = next;
                    Prune(counter);
                    return next;
                }
                catch (RequestFailedException e) when (e.Status is 409 or 412 && attempt < 3)
                {
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(long Count, ETag? ETag)> Read(string counter, CancellationToken ct)
    {
        await _ensureContainer.Value;
        try
        {
            var result = await _container.GetBlobClient($"{counter}.json").DownloadContentAsync(ct);
            return (result.Value.Content.ToObjectFromJson<long>(), result.Value.Details.ETag);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return (0, null);
        }
    }

    // Counter names start with their date ("2026-11-16/..."), so older days can be dropped from memory.
    private void Prune(string current)
    {
        var today = current.Split('/')[0];
        foreach (var key in _counts.Keys)
            if (!key.StartsWith(today, StringComparison.Ordinal))
                _counts.TryRemove(key, out _);
    }
}
