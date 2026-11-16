using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Share;

/// <summary>
/// Organizer: opens the bill for claiming. Refused with 422 until every receipt matches its
/// printed total. Unclaimed items don't block sharing; claiming them is what sharing is for.
/// </summary>
public static class ShareBill
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/share", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Share", requireIfMatch: false, bill =>
        {
            if (bill.Receipts.Count == 0)
                return OpError.Unprocessable("no-receipts");
            if (bill.Receipts.Any(r => r.Lines.Count == 0))
                return OpError.Unprocessable("empty-receipt");
            if (bill.People.Count == 0)
                return OpError.Unprocessable("no-people");
            if (!BillResponse.CanShare(bill))
                return OpError.Unprocessable("total-mismatch");

            if (bill.Status != BillStatus.Shared)
                bill.Log(access.Clock.GetUtcNow(), ActivityKind.Share, "The bill was shared");
            bill.Status = BillStatus.Shared;
            return null;
        });
}
