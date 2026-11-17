using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Lock;

/// <summary>Organizer: undoes a lock, so people can claim and pay again. Goes back to shared, or to draft if a receipt no longer matches.</summary>
public static class ReopenBill
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/reopen", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Reopen", requireIfMatch: false, bill =>
        {
            if (bill.Status != BillStatus.Locked)
                return null;
            bill.Status = BillResponse.CanShare(bill) ? BillStatus.Shared : BillStatus.Draft;
            bill.LockedAt = null;
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Reopen, "The bill was reopened");
            return null;
        }, allowLocked: true);
}
