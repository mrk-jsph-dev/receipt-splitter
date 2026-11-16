using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.DeleteBill;

/// <summary>
/// Organizer: deletes the bill now instead of 30 days after the last change. Works on a locked bill.
/// Afterwards the link gives the same 404 as a bill that never existed.
/// </summary>
public static class DeleteBill
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/bills/{id}", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static async Task<IResult> Handle(string id, HttpContext http, BillAccess access, IBillStore store, PrivacyHash hash, ILogger<BillAccess> log)
    {
        var (stored, error) = await access.LoadAsync(http, id);
        if (error is not null)
            return error;
        if (access.AuthorizeOrganizer(http, stored!.Bill, requireIfMatch: false) is { } denied)
            return denied;

        await store.DeleteAsync(stored.Bill.Id, http.RequestAborted);
        log.LogInformation("Bill {BillHash} deleted", await hash.OfAsync(stored.Bill.Id));
        return Results.NoContent();
    }
}
