using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.CreateBill;

public enum BillSource { Sample, Manual, Scan }

/// <param name="OrganizerName">The name saved in the organizer's browser. Becomes the first person on a manual bill.</param>
/// <param name="Currency">Required for manual bills. The sample is always PHP.</param>
/// <param name="Draft">An optional first receipt for a manual bill.</param>
public sealed record CreateBillRequest(
    BillSource Source,
    string? OrganizerName = null,
    CurrencyInfo? Currency = null,
    string? Title = null,
    ReceiptDraft? Draft = null);

/// <summary>The only time the organizer key is ever sent. The server keeps just its hash.</summary>
public sealed record CreateBillResponse(string Id, string OrganizerKey);

public static class CreateBill
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills", Handle).RequireRateLimiting(RateLimitPolicies.Create);

    private static async Task<IResult> Handle(
        CreateBillRequest request,
        HttpContext http,
        IBillStore store,
        TimeProvider clock,
        IOptions<BillLimits> limits,
        PrivacyHash hash,
        CreateCap cap,
        ILogger<CreateBillRequest> log)
    {
        if (await cap.IsReachedAsync(http.RequestAborted))
            return ApiResults.TooMany("daily-cap");

        var (bill, error) = request.Source == BillSource.Sample
            ? FromSample(limits.Value)
            : FromManual(request, limits.Value);
        if (error is not null)
            return ApiResults.Invalid(error.Reason);

        var organizerKey = SecureIds.NewSecret();
        var now = clock.GetUtcNow();
        bill!.OrganizerKeyHash = OrganizerKey.Hash(organizerKey);
        bill.Version = 1;
        bill.CreatedAt = bill.UpdatedAt = now;

        // A clash between two random 128-bit ids will not happen, but a retry costs nothing.
        while (!await store.CreateAsync(bill, http.RequestAborted))
            bill.Id = SecureIds.NewSecret();
        await cap.RecordAsync(http.RequestAborted);

        log.LogInformation("Bill {BillHash} created from {Source}", await hash.OfAsync(bill.Id), request.Source);
        http.Response.Headers.ETag = ApiResults.ETagOf(bill);
        return Results.Created($"/api/bills/{bill.Id}", new CreateBillResponse(bill.Id, organizerKey));
    }

    private static (BillDocument?, OpError?) FromSample(BillLimits limits)
    {
        var sample = SampleBill.Value;
        var people = sample.People.Select(name => new PersonDoc { Id = SecureIds.NewShort(), Name = name }).ToList();
        var (receipt, _, error) = ReceiptDrafts.ToDocument(sample.Receipt, ReceiptSource.Sample, limits, people);
        if (error is not null)
            throw new InvalidOperationException($"The bundled sample is invalid: {error.Reason}.");

        receipt!.Payers = [new PayerDoc(people[0].Id, receipt.ConfirmedTotal)];
        return (NewBill(sample.Title, sample.Currency, people, receipt, sample.HeadcountHint), null);
    }

    private static (BillDocument?, OpError?) FromManual(CreateBillRequest request, BillLimits limits)
    {
        if (request.Currency is not { } currency || !currency.IsValid())
            return (null, OpError.Invalid("currency"));

        var people = new List<PersonDoc>();
        if (request.OrganizerName is not null)
        {
            var name = Text.Clean(request.OrganizerName, Text.MaxPersonName);
            if (name is null)
                return (null, OpError.Invalid("name"));
            // The organizer acts with the key, so nobody needs this hold's token; it only keeps
            // another phone from picking the organizer's name.
            people.Add(new PersonDoc { Id = SecureIds.NewShort(), Name = name, HoldHash = PersonToken.New().Hash });
        }

        ReceiptDoc? receipt = null;
        if (request.Draft is not null)
        {
            (receipt, _, var error) = ReceiptDrafts.ToDocument(request.Draft, request.Source == BillSource.Scan ? ReceiptSource.Scan : ReceiptSource.Manual, limits, people);
            if (error is not null)
                return (null, error);
            if (people.Count > 0 && request.Draft.Payers is null)
                receipt!.Payers = [new PayerDoc(people[0].Id, receipt.ConfirmedTotal)];
        }

        var title = request.Title is null ? null : Text.Clean(request.Title, Text.MaxLabel);
        if (request.Title is not null && title is null)
            return (null, OpError.Invalid("title"));

        // Suggested from the first receipt; the organizer can rename it any time.
        title ??= receipt?.Merchant ?? receipt?.Label ?? "";
        return (NewBill(title, currency, people, receipt, null), null);
    }

    private static BillDocument NewBill(string title, CurrencyInfo currency, List<PersonDoc> people, ReceiptDoc? receipt, int? headcountHint) => new()
    {
        Id = SecureIds.NewSecret(),
        Title = title,
        Currency = currency,
        Status = BillStatus.Draft,
        People = people,
        HeadcountHint = headcountHint,
        Receipts = receipt is null ? [] : [receipt],
        OrganizerKeyHash = "",
    };
}
