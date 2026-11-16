using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Settings;

/// <param name="PaymentNote">Free text such as "GCash 09xx / BPI ...", up to 280 characters. Null or empty clears it.</param>
public sealed record SettingsRequest(string Title, CurrencyInfo Currency, string? PaymentNote = null);

/// <summary>
/// Organizer: bill name, currency and payment note. The currency can only change while the bill
/// has no receipts, because stored amounts are never re-scaled to other minor-unit digits.
/// </summary>
public static class UpdateSettings
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/api/bills/{id}/settings", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, SettingsRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "UpdateSettings", requireIfMatch: true, bill =>
        {
            var title = Text.Clean(request.Title, Text.MaxLabel);
            if (title is null)
                return OpError.Invalid("title");
            if (!request.Currency.IsValid())
                return OpError.Invalid("currency");
            if (request.Currency != bill.Currency && bill.Receipts.Count > 0)
                return OpError.Invalid("currency-has-receipts");

            // Line breaks are fine in a payment note; other control characters are not.
            var note = request.PaymentNote?.Trim();
            if (note is { Length: > Text.MaxPaymentNote } || note?.Any(c => char.IsControl(c) && c != '\n') == true)
                return OpError.Invalid("payment-note");

            bill.Title = title;
            bill.Currency = request.Currency;
            bill.PaymentNote = string.IsNullOrEmpty(note) ? null : note;
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Settings, "Bill details were updated");
            return null;
        });
}
