using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;

namespace ReceiptSplitter.Api.Shared;

/// <summary>Size limits per bill. Values come from the "Bills" config section.</summary>
public sealed class BillLimits
{
    public int RetentionDays { get; set; } = 30;
    public int MaxPeople { get; set; } = 50;
    public int MaxReceipts { get; set; } = 10;
    public int MaxLinesPerReceipt { get; set; } = 200;
    public int MaxUnitsPerLine { get; set; } = 100;
    public int MaxActivity { get; set; } = 100;
}

/// <summary>Why an op was refused. 400 means the request was wrong; 409 means the bill moved on.</summary>
public sealed record OpError(int Status, string Reason)
{
    public static OpError Invalid(string reason) => new(StatusCodes.Status400BadRequest, reason);
    public static OpError Conflict(string reason) => new(StatusCodes.Status409Conflict, reason);
    public static OpError Unprocessable(string reason) => new(StatusCodes.Status422UnprocessableEntity, reason);
}

public sealed record ErrorBody(string Error, string? Reason = null);

/// <summary>A refusal that comes with the latest bill, so the client can show what changed.</summary>
public sealed record ConflictBody(string Error, string Reason, BillView Bill, BreakdownView? Breakdown = null);

public static class ApiResults
{
    private static readonly ErrorBody NotFoundBody = new("not-found");

    /// <summary>The one 404 for unknown, malformed, deleted and expired bills, so probing learns nothing.</summary>
    public static IResult NotFound() => Results.Json(NotFoundBody, statusCode: StatusCodes.Status404NotFound);

    public static IResult TooMany(string reason) => Results.Json(new ErrorBody("rate-limited", reason), statusCode: StatusCodes.Status429TooManyRequests);

    public static IResult Invalid(string reason) => Results.Json(new ErrorBody("invalid", reason), statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The bill has a PIN and this request carries neither its token nor the organizer key.</summary>
    public static IResult PinRequired() => Results.Json(new ErrorBody("pin-required"), statusCode: StatusCodes.Status401Unauthorized);

    public static IResult Forbidden() => Results.Json(new ErrorBody("forbidden"), statusCode: StatusCodes.Status403Forbidden);

    public static IResult IfMatchRequired() => Results.Json(new ErrorBody("precondition-required", "if-match"), statusCode: StatusCodes.Status428PreconditionRequired);

    public static IResult Conflict(BillDocument bill, string reason, int retentionDays) =>
        WithBill(bill, StatusCodes.Status409Conflict, reason, retentionDays);

    public static IResult Refused(BillDocument bill, OpError error, int retentionDays) =>
        error.Status == StatusCodes.Status400BadRequest ? Invalid(error.Reason) : WithBill(bill, error.Status, error.Reason, retentionDays);

    public static string ETagOf(BillDocument bill) => $"\"{bill.Version}\"";

    private static IResult WithBill(BillDocument bill, int status, string reason, int retentionDays)
    {
        var error = status switch
        {
            StatusCodes.Status412PreconditionFailed => "precondition-failed",
            StatusCodes.Status422UnprocessableEntity => "cannot-share",
            _ => "conflict",
        };
        var response = BillResponse.From(bill, retentionDays);
        return Results.Json(new ConflictBody(error, reason, response.Bill, response.Breakdown), statusCode: status);
    }
}

/// <summary>
/// Every read and write of one bill goes through here: the not-found guard, the identical 404,
/// expiry, the organizer key, and read-modify-write with storage ETags.
/// </summary>
public sealed class BillAccess(
    IBillStore store,
    NotFoundGuard guard,
    PrivacyHash hash,
    TimeProvider clock,
    IOptions<BillLimits> limits,
    ILogger<BillAccess> log)
{
    private const int WriteAttempts = 4;

    public BillLimits Limits => limits.Value;

    public TimeProvider Clock => clock;

    public async Task<(StoredBill? Stored, IResult? Error)> LoadAsync(HttpContext http, string id)
    {
        if (await guard.IsBlockedAsync(http))
            return (null, ApiResults.TooMany("not-found"));

        var stored = SecureIds.IsSecret(id) ? await store.GetAsync(id, http.RequestAborted) : null;
        if (stored is null || IsExpired(stored.Bill))
        {
            await guard.RecordAsync(http);
            return (null, ApiResults.NotFound());
        }
        return (stored, null);
    }

    /// <summary>
    /// The organizer key from the X-Organizer-Key header only, never the query string. With
    /// <paramref name="requireIfMatch"/> the client must also say which version it edited.
    /// </summary>
    public IResult? AuthorizeOrganizer(HttpContext http, BillDocument bill, bool requireIfMatch)
    {
        var keys = http.Request.Headers[OrganizerKey.Header];
        if (keys.Count != 1 || !OrganizerKey.Matches(keys[0], bill.OrganizerKeyHash))
            return ApiResults.Forbidden();

        var ifMatch = http.Request.GetTypedHeaders().IfMatch;
        if (ifMatch.Count == 0)
            return requireIfMatch ? ApiResults.IfMatchRequired() : null;
        var etag = ApiResults.ETagOf(bill);
        return ifMatch.Any(tag => tag.Tag == etag || tag.Tag == "*")
            ? null
            : ApiResults.Refused(bill, new OpError(StatusCodes.Status412PreconditionFailed, "changed"), Limits.RetentionDays);
    }

    /// <summary>
    /// A link holder on a bill with a PIN needs the token from unlocking it (X-Bill-Pin), or the
    /// organizer key. Checked after the bill is found, so unknown bills still get the one 404.
    /// </summary>
    public IResult? AuthorizeLinkHolder(HttpContext http, BillDocument bill)
    {
        if (bill.PinHash is null)
            return null;
        var keys = http.Request.Headers[OrganizerKey.Header];
        if (keys.Count == 1 && OrganizerKey.Matches(keys[0], bill.OrganizerKeyHash))
            return null;
        var tokens = http.Request.Headers[BillPin.Header];
        return tokens.Count == 1 && BillPin.TokenMatches(tokens[0], bill.PinHash, bill.Id) ? null : ApiResults.PinRequired();
    }

    /// <summary>A link holder's op. See <see cref="WriteAsync"/>.</summary>
    public Task<IResult> UpdateAsync(HttpContext http, string id, string operation, Func<BillDocument, OpError?> apply) =>
        WriteAsync(http, id, operation, organizer: false, requireIfMatch: false, apply);

    /// <summary>
    /// An organizer's op. If it leaves a shared bill with a receipt that no longer matches its
    /// total, the bill goes back to draft until it matches again.
    /// </summary>
    public Task<IResult> OrganizerUpdateAsync(HttpContext http, string id, string operation, bool requireIfMatch, Func<BillDocument, OpError?> apply) =>
        WriteAsync(http, id, operation, organizer: true, requireIfMatch, apply);

    /// <summary>
    /// Applies one op and saves it. <paramref name="apply"/> must check everything before it changes
    /// the bill, so a refused op leaves the bill as it was read. A storage clash is retried here with
    /// fresh data; the client only sees 409 when its own <c>from</c> no longer holds.
    /// </summary>
    private async Task<IResult> WriteAsync(HttpContext http, string id, string operation, bool organizer, bool requireIfMatch, Func<BillDocument, OpError?> apply)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (stored, error) = await LoadAsync(http, id);
            if (error is not null)
                return error;

            var bill = stored!.Bill;
            if (organizer && AuthorizeOrganizer(http, bill, requireIfMatch) is { } denied)
                return denied;
            if (!organizer && AuthorizeLinkHolder(http, bill) is { } locked)
                return locked;
            if (bill.Status == BillStatus.Locked)
                return ApiResults.Conflict(bill, "locked", Limits.RetentionDays);

            var opError = apply(bill);
            if (opError is not null)
                return ApiResults.Refused(bill, opError, Limits.RetentionDays);

            if (organizer && bill.Status == BillStatus.Shared && !BillResponse.CanShare(bill))
                bill.Status = BillStatus.Draft;
            bill.Version++;
            bill.UpdatedAt = clock.GetUtcNow();
            if (bill.Activity.Count > Limits.MaxActivity)
                bill.Activity.RemoveRange(0, bill.Activity.Count - Limits.MaxActivity);

            if (await store.ReplaceAsync(bill, stored.ETag, http.RequestAborted))
            {
                log.LogInformation("Bill {BillHash} {Operation} saved at version {Version}", await hash.OfAsync(bill.Id), operation, bill.Version);
                return Ok(http, bill);
            }

            if (attempt == WriteAttempts)
            {
                var (latest, _) = await LoadAsync(http, id);
                return latest is null ? ApiResults.NotFound() : ApiResults.Conflict(latest.Bill, "busy", Limits.RetentionDays);
            }
            await Task.Delay(Random.Shared.Next(10, 40) * attempt, http.RequestAborted);
        }
    }

    public IResult Ok(HttpContext http, BillDocument bill)
    {
        http.Response.Headers.ETag = ApiResults.ETagOf(bill);
        return Results.Ok(BillResponse.From(bill, Limits.RetentionDays));
    }

    private bool IsExpired(BillDocument bill) => clock.GetUtcNow() - bill.UpdatedAt > TimeSpan.FromDays(Limits.RetentionDays);
}
