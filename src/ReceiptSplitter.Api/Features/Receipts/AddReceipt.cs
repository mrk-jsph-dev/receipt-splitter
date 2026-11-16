using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Receipts;

/// <param name="Draft">Required unless the source is the sample.</param>
public sealed record AddReceiptRequest(ReceiptSource Source, ReceiptDraft? Draft = null);

/// <summary>Organizer: dinner, then drinks, then dessert in one bill. Each receipt keeps its own fees and payers.</summary>
public static class AddReceipt
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/receipts", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, AddReceiptRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "AddReceipt", requireIfMatch: false, bill =>
        {
            var draft = request.Source == ReceiptSource.Sample ? SampleBill.Value.Receipt : request.Draft;
            if (draft is null)
                return OpError.Invalid("draft");
            if (bill.Receipts.Count >= access.Limits.MaxReceipts)
                return OpError.Invalid("too-many-receipts");

            var (receipt, _, error) = ReceiptDrafts.ToDocument(draft, request.Source, access.Limits, bill.People);
            if (error is not null)
                return error;

            bill.Receipts.Add(receipt!);
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.AddReceipt, $"{receipt!.Label} was added");
            return null;
        });
}
