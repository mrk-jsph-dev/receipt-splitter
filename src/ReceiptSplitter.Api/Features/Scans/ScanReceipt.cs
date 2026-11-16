using System.Text.Json;
using Microsoft.Extensions.Options;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Scanning;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Scans;

/// <param name="Draft">Lines, fees and discounts, merchant, date and printed total, for the organizer to check.</param>
/// <param name="RemainingToday">Scans left today across everyone.</param>
/// <param name="ReadCurrency">The currency code printed on the receipt, when the model found one.</param>
public sealed record ScanResponse(ReceiptDraft Draft, int RemainingToday, string? ReadCurrency);

public sealed record ScanQuota(int RemainingToday);

/// <summary>
/// Anyone: reads one receipt photo or PDF into a draft. The file is held in memory for this request,
/// sent to Document Intelligence and dropped. It is never stored or logged, and the draft is not
/// saved until the organizer creates the bill. Form fields: file, currencyCode, currencyDigits, and
/// previous (a draft from the first photo of a long receipt, to append to).
/// </summary>
public static class ScanReceipt
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/scans", Handle).RequireRateLimiting(RateLimitPolicies.Scan).DisableAntiforgery();
        app.MapGet("/api/scans/quota", async (ScanCap cap, HttpContext http) => Results.Ok(new ScanQuota(await cap.RemainingAsync(http.RequestAborted))));
    }

    private static async Task<IResult> Handle(HttpContext http, IReceiptReader reader, ScanCap cap, IOptions<ScanOptions> options, ILogger<ScanResponse> log)
    {
        var ct = http.RequestAborted;
        if (!http.Request.HasFormContentType)
            return ApiResults.Invalid("form");
        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return ApiResults.Invalid("file");
        if (file.Length > options.Value.MaxBytes)
            return Results.Json(new ErrorBody("too-large", "file"), statusCode: StatusCodes.Status413PayloadTooLarge);

        if (!int.TryParse(form["currencyDigits"], out var digits))
            digits = 2;
        var currency = new CurrencyInfo(form["currencyCode"].ToString() is { Length: > 0 } code ? code : "PHP", digits);
        if (!currency.IsValid())
            return ApiResults.Invalid("currency");

        ReceiptDraft? previous = null;
        if (form["previous"].ToString() is { Length: > 0 } previousJson)
        {
            try { previous = JsonSerializer.Deserialize<ReceiptDraft>(previousJson, ApiJson.Options); }
            catch (JsonException) { return ApiResults.Invalid("previous"); }
        }

        // Read into memory once. Nothing below writes these bytes anywhere but the reader.
        byte[] bytes;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        if (FileKind.Of(bytes) is null)
            return Results.Json(new ErrorBody("unsupported", "file-type"), statusCode: StatusCodes.Status415UnsupportedMediaType);

        if (await cap.RemainingAsync(ct) <= 0)
            return ApiResults.TooMany("daily-cap");
        await cap.RecordAsync(ct);

        ScannedReceipt scan;
        try
        {
            scan = await reader.ReadAsync(BinaryData.FromBytes(bytes), ct);
        }
        catch (ScanUnavailableException e)
        {
            log.LogWarning("Scan failed: {Reason}", e.Message);
            return Results.Json(new ErrorBody("scan-unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            Array.Clear(bytes);
        }

        var draft = ScanClassifier.ToDraft(scan, currency);
        if (previous is not null)
            draft = ScanClassifier.Append(previous, draft);
        log.LogInformation("Scan read {Lines} lines and {Adjustments} adjustments", draft.Lines.Count, draft.Adjustments?.Count ?? 0);
        return Results.Ok(new ScanResponse(draft, await cap.RemainingAsync(ct), scan.CurrencyCode));
    }
}

/// <summary>Checks the first bytes, not the name or the declared type, which the browser picks.</summary>
public static class FileKind
{
    public static string? Of(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (b.Length >= 5 && b[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (b.Length >= 12 && b.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = System.Text.Encoding.ASCII.GetString(b.Slice(8, 4));
            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1") return "image/heif";
        }
        if (b.Length >= 4 && (b[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) || b[..4].SequenceEqual(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }))) return "image/tiff";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return "image/bmp";
        return null;
    }
}
