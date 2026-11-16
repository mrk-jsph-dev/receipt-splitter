using System.Text.Json;
using ReceiptSplitter.Api.Infrastructure.Http;

namespace ReceiptSplitter.Api.Shared;

/// <summary>samples/sample-receipt.json, embedded in the API: the Ramen Nagi receipt with made-up names.</summary>
public sealed record SampleBill(string Title, CurrencyInfo Currency, int HeadcountHint, IReadOnlyList<string> People, ReceiptDraft Receipt)
{
    private static readonly Lazy<SampleBill> File = new(() =>
    {
        using var stream = typeof(SampleBill).Assembly.GetManifestResourceStream("sample-receipt.json")
            ?? throw new InvalidOperationException("The sample receipt is not embedded.");
        return JsonSerializer.Deserialize<SampleBill>(stream, ApiJson.Options)!;
    });

    public static SampleBill Value => File.Value;
}
