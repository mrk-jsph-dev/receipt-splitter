using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Receipts;

/// <summary>
/// Organizer: replaces a receipt's lines, fees, participants, payers, split and confirmed total.
/// Lines sent with their id keep their claims; claims on units that went away are removed and the
/// people affected see a notice. Needs If-Match, so two organizer tabs can't overwrite each other.
/// </summary>
public static class ReplaceReceipt
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/api/bills/{id}/receipts/{receiptId}", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, string receiptId, ReceiptDraft draft, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "ReplaceReceipt", requireIfMatch: true, bill =>
        {
            var index = bill.Receipts.FindIndex(r => r.Id == receiptId);
            if (index < 0)
                return OpError.Invalid("unknown-receipt");

            var existing = bill.Receipts[index];
            var (receipt, dropped, error) = ReceiptDrafts.ToDocument(draft, existing.Source, access.Limits, bill.People, existing);
            if (error is not null)
                return error;

            var now = access.Clock.GetUtcNow();
            bill.Receipts[index] = receipt!;
            bill.Log(now, ActivityKind.EditReceipt, $"{receipt!.Label} was edited");
            RemovedClaims.Log(bill, now, dropped);
            return null;
        });
}

public static class RemovedClaims
{
    public static void Log(BillDocument bill, DateTimeOffset now, IEnumerable<DroppedClaim> dropped)
    {
        foreach (var claim in dropped)
            bill.Log(now, ActivityKind.ClaimRemoved, $"{bill.NameOf(claim.PersonId)}'s claim on {claim.LineName} was removed", claim.PersonId, claim.UnitId);
    }
}
