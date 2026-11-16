using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Receipts;

/// <summary>Organizer: removes a receipt. Its claims go with it, and the people affected see a notice.</summary>
public static class DeleteReceipt
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/bills/{id}/receipts/{receiptId}", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, string receiptId, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "DeleteReceipt", requireIfMatch: true, bill =>
        {
            var receipt = bill.Receipts.Find(r => r.Id == receiptId);
            if (receipt is null)
                return OpError.Invalid("unknown-receipt");

            var now = access.Clock.GetUtcNow();
            bill.Receipts.Remove(receipt);
            bill.Log(now, ActivityKind.RemoveReceipt, $"{receipt.Label} was removed");
            RemovedClaims.Log(bill, now,
                receipt.Lines.SelectMany(l => l.Units.SelectMany(u => u.Claims.Select(c => new DroppedClaim(c.PersonId, u.Id, l.Name)))));
            return null;
        });
}
