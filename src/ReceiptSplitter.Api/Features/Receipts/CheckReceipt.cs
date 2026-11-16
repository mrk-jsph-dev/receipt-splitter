using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Receipts;

namespace ReceiptSplitter.Api.Features.Receipts;

/// <param name="ReceiptId">The receipt being edited, so lines sent with an id keep their claims in the preview.</param>
public sealed record CheckReceiptRequest(ReceiptDraft Draft, string? ReceiptId = null);

/// <param name="Items">Σ line totals.</param>
/// <param name="LineTotals">Each line's total, in order: quantity × price, or the printed total when given.</param>
/// <param name="Adjustments">Σ fees and discounts that are not already in the prices.</param>
/// <param name="Suggested">Per adjustment, in order: the amount its percent gives, or null when it has no percent.</param>
/// <param name="Breakdown">Who would pay what, once the bill has people.</param>
public sealed record ReceiptCheck(
    long Items,
    IReadOnlyList<long> LineTotals,
    long Adjustments,
    long ComputedTotal,
    long ConfirmedTotal,
    long Difference,
    bool Matches,
    IReadOnlyList<long?> Suggested,
    ReceiptBreakdown? Breakdown);

/// <summary>
/// Organizer: the total check while a receipt is being typed or fixed. Nothing is saved. The
/// editor sends its draft as the organizer types, so the browser never adds money up itself.
/// </summary>
public static class CheckReceipt
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/receipts/check", Handle).RequireRateLimiting(RateLimitPolicies.Read);

    private static async Task<IResult> Handle(string id, CheckReceiptRequest request, HttpContext http, BillAccess access)
    {
        var (stored, error) = await access.LoadAsync(http, id);
        if (error is not null)
            return error;
        var bill = stored!.Bill;
        if (access.AuthorizeOrganizer(http, bill, requireIfMatch: false) is { } denied)
            return denied;
        if (request.Draft is null)
            return ApiResults.Invalid("draft");

        var existing = request.ReceiptId is null ? null : bill.Receipts.Find(r => r.Id == request.ReceiptId);
        if (request.ReceiptId is not null && existing is null)
            return ApiResults.Invalid("unknown-receipt");

        var (receipt, _, invalid) = ReceiptDrafts.ToDocument(request.Draft, existing?.Source ?? ReceiptSource.Manual, access.Limits, bill.People, existing);
        if (invalid is not null)
            return ApiResults.Invalid(invalid.Reason);

        return Results.Ok(Check(receipt!, bill.People));
    }

    public static ReceiptCheck Check(ReceiptDoc receipt, IReadOnlyList<PersonDoc> people)
    {
        var core = CoreMapping.ToCore(receipt, people);
        var items = core.Lines.Sum(l => l.LineTotal);
        var adjustments = core.Adjustments.Where(a => !a.IncludedInPrices).Sum(a => a.Amount);
        var computed = checked(items + adjustments);
        var suggested = receipt.Adjustments
            .Select(a => a.Rate is { } rate ? AdjustmentRates.Suggest(core.Lines, a.Kind, rate, a.Scope?.Units) : (long?)null)
            .ToList();
        var breakdown = people.Count > 0 ? ReceiptCalculator.Calculate(people.Select(CoreMapping.ToCore).ToList(), core) : null;
        var difference = computed - receipt.ConfirmedTotal;
        return new ReceiptCheck(items, core.Lines.Select(l => l.LineTotal).ToList(), adjustments, computed, receipt.ConfirmedTotal, difference, difference == 0, suggested, breakdown);
    }
}
