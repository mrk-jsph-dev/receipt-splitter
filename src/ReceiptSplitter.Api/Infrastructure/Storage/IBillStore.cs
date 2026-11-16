using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Infrastructure.Storage;

/// <summary>A stored bill and the storage ETag it was read at.</summary>
public sealed record StoredBill(BillDocument Bill, string ETag);

/// <summary>
/// One document per bill, read and written by exact id. There is deliberately no way to list bills.
/// </summary>
public interface IBillStore
{
    Task<StoredBill?> GetAsync(string id, CancellationToken ct);

    /// <summary>False if a bill with this id already exists.</summary>
    Task<bool> CreateAsync(BillDocument bill, CancellationToken ct);

    /// <summary>Writes only if the stored bill still has <paramref name="etag"/>. False if it changed or is gone.</summary>
    Task<bool> ReplaceAsync(BillDocument bill, string etag, CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}

/// <summary>
/// Small state for abuse guards, kept apart from bills: a salt for hashing ids and IPs, and
/// counters per fixed time window. Lives under limits/, which a lifecycle rule clears after 2 days.
/// </summary>
public interface ILimitStore
{
    Task<byte[]> GetSaltAsync(CancellationToken ct);

    Task<long> GetCountAsync(string counter, CancellationToken ct);

    /// <summary>Adds one and returns the new count.</summary>
    Task<long> IncrementAsync(string counter, CancellationToken ct);
}
