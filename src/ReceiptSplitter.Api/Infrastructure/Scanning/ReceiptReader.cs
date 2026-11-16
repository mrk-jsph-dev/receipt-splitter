using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;

namespace ReceiptSplitter.Api.Infrastructure.Scanning;

/// <summary>What the receipt model read, before our classifier turns it into a draft. Amounts as printed.</summary>
public sealed record ScannedReceipt(
    string? Merchant,
    DateOnly? Date,
    string? CurrencyCode,
    decimal? Total,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Tip,
    IReadOnlyList<ScannedItem> Items);

/// <param name="Text">The item's description as printed.</param>
/// <param name="Confidence">0 to 1: the lowest of the description and price confidences.</param>
public sealed record ScannedItem(string Text, decimal? Quantity, decimal? Price, decimal? TotalPrice, double Confidence);

/// <summary>Reads one receipt photo or PDF. The bytes stay in memory for the call and are never kept.</summary>
public interface IReceiptReader
{
    Task<ScannedReceipt> ReadAsync(BinaryData file, CancellationToken ct);
}

/// <summary>Raised when the reader can't be reached or refuses, so the endpoint can say "type it in instead".</summary>
public sealed class ScanUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Azure AI Document Intelligence, prebuilt receipt model (API 2024-11-30). Signs in with the app's
/// managed identity; there is no key anywhere. Only the fields below are read; raw text, card
/// digits and staff names in the result are dropped here.
/// </summary>
public sealed class DocumentIntelligenceReader(DocumentIntelligenceClient client) : IReceiptReader
{
    public static DocumentIntelligenceReader For(Uri endpoint) => new(new DocumentIntelligenceClient(endpoint, new DefaultAzureCredential()));

    public async Task<ScannedReceipt> ReadAsync(BinaryData file, CancellationToken ct)
    {
        AnalyzeResult result;
        try
        {
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, new AnalyzeDocumentOptions("prebuilt-receipt", file), ct);
            result = operation.Value;
        }
        catch (RequestFailedException e)
        {
            // The message carries no request content, only the service's status and code.
            throw new ScanUnavailableException($"Document Intelligence refused: {e.Status} {e.ErrorCode}", e);
        }

        var fields = result.Documents.FirstOrDefault()?.Fields;
        if (fields is null)
            return new ScannedReceipt(null, null, null, null, null, null, null, []);

        var items = new List<ScannedItem>();
        if (fields.TryGetValue("Items", out var list) && list.ValueList is { } entries)
        {
            foreach (var entry in entries)
            {
                var item = entry.ValueDictionary;
                if (item is null)
                    continue;
                var text = Str(item, "Description");
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var confidence = new[] { entry.Confidence, Field(item, "Description")?.Confidence, Field(item, "TotalPrice")?.Confidence }
                    .Where(c => c is not null).Select(c => (double)c!.Value).DefaultIfEmpty(0).Min();
                items.Add(new ScannedItem(text.Trim(), Number(item, "Quantity"), Money(item, "Price"), Money(item, "TotalPrice"), confidence));
            }
        }

        return new ScannedReceipt(
            Str(fields, "MerchantName"),
            fields.TryGetValue("TransactionDate", out var date) && date.ValueDate is { } d ? DateOnly.FromDateTime(d.Date) : null,
            Field(fields, "Total")?.ValueCurrency?.CurrencyCode,
            Money(fields, "Total"),
            Money(fields, "Subtotal"),
            Money(fields, "TotalTax"),
            Money(fields, "Tip"),
            items);
    }

    private static DocumentField? Field(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        fields.TryGetValue(name, out var field) ? field : null;

    private static string? Str(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        Field(fields, name) is { } f ? f.ValueString ?? f.Content : null;

    // The SDK gives doubles. Going through decimal with rounding to 4 places keeps 195.00 as 195.00;
    // the caller rounds to the currency's digits and stores whole minor units.
    private static decimal? Money(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        Field(fields, name) is { } f
            ? f.ValueCurrency is { } c ? decimal.Round((decimal)c.Amount, 4) : f.ValueDouble is { } v ? decimal.Round((decimal)v, 4) : null
            : null;

    private static decimal? Number(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        Field(fields, name) is { } f ? f.ValueDouble is { } v ? decimal.Round((decimal)v, 4) : f.ValueInt64 is { } i ? i : null : null;
}

/// <summary>
/// For running locally with no Azure: every photo "reads" as the Ramen Nagi receipt, with the
/// Kakuni Belly misread as 105.00 like the walkthrough. Never used in production.
/// </summary>
public sealed class SampleReceiptReader : IReceiptReader
{
    public Task<ScannedReceipt> ReadAsync(BinaryData file, CancellationToken ct) => Task.FromResult(new ScannedReceipt(
        "Ramen Nagi", new DateOnly(2026, 9, 25), "PHP", 5255.80m, null, null, null,
        [
            new("Butao King", 8, 485.00m, 3880.00m, 0.98), new("Classic Gyoza", 1, 190.00m, 190.00m, 0.97),
            new("Coke Zero", 1, 115.00m, 115.00m, 0.97), new("Kaedama (thick)", 1, 95.00m, 95.00m, 0.95),
            new("Kaedama (thin)", 3, 95.00m, 285.00m, 0.95), new("Gyu King", 2, 795.00m, 1590.00m, 0.98),
            new("Kakuni Belly", 1, 105.00m, 105.00m, 0.62), new("Red King", 2, 595.00m, 1190.00m, 0.97),
            new("Tamago", 1, 85.00m, 85.00m, 0.96), new("SERVICE CHARGE", null, null, 680.80m, 0.93),
            new("40% BDO OFF", null, null, 3050.00m, 0.91),
        ]));
}

/// <summary>No reader configured: scanning says it is unavailable and the app offers typing it in.</summary>
public sealed class NoReader : IReceiptReader
{
    public Task<ScannedReceipt> ReadAsync(BinaryData file, CancellationToken ct) =>
        throw new ScanUnavailableException("No receipt reader is configured.");
}
