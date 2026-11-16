using System.Globalization;
using System.Text.RegularExpressions;
using ReceiptSplitter.Api.Infrastructure.Scanning;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Features.Scans;

/// <summary>
/// Turns what the receipt model read into a draft. Service charges and discounts are not receipt
/// fields, so they arrive as items; their words and sign move them into fees and discounts
/// (docs/SPEC.md "How a scan finds fees and discounts"). Every guess is flagged for review in the
/// editor, and the total check catches anything missed. Nothing is ever auto-confirmed.
/// </summary>
public static partial class ScanClassifier
{
    /// <summary>Lines read with less confidence than this are flagged "not sure about this price".</summary>
    public const double Unsure = 0.8;

    public static ReceiptDraft ToDraft(ScannedReceipt scan, CurrencyInfo currency)
    {
        var lines = new List<LineDraft>();
        var adjustments = new List<AdjustmentDraft>();

        foreach (var item in scan.Items)
        {
            var text = Clean(item.Text);
            if (text is null)
                continue;
            var total = Minor(item.TotalPrice ?? (item.Price is { } p ? p * (item.Quantity ?? 1) : null), currency);
            if (total is null)
                continue;

            if (Classify(text, total.Value) is { } kind)
            {
                var amount = Core.Receipts.AdjustmentRates.Reduces(kind) ? -Math.Abs(total.Value) : total.Value;
                adjustments.Add(new AdjustmentDraft(
                    kind, text, amount, RateIn(text),
                    // Philippine VAT lines restate tax already inside the prices.
                    IncludedInPrices: kind == AdjustmentKind.Tax && VatWords().IsMatch(text),
                    Split: kind == AdjustmentKind.Service ? AdjustmentSplit.Equal : AdjustmentSplit.Proportional));
                continue;
            }

            var quantity = item.Quantity is { } q && q >= 1 && q <= 100 && q == decimal.Truncate(q) ? (int)q : 1;
            // The printed line total is the truth. A unit price that doesn't multiply out (a deal like
            // 3 for 100.00, or a misread) keeps the printed total, which the editor shows.
            long unit;
            long? lineTotal = null;
            if (Minor(item.Price, currency) is { } price && quantity * price == total.Value)
                unit = price;
            else if (total.Value % quantity == 0)
                unit = total.Value / quantity;
            else
            {
                unit = Minor(item.Price, currency) ?? total.Value / quantity;
                lineTotal = total.Value;
            }
            lines.Add(new LineDraft(text, quantity, unit, lineTotal, (decimal)Math.Round(Math.Clamp(item.Confidence, 0, 1), 2)));
        }

        // Tip and tax come as their own fields when printed that way. Tax is counted on top only when
        // the receipt's own numbers say so; otherwise it is shown as already in the prices.
        var subtotal = Minor(scan.Subtotal, currency);
        var printedTotal = Minor(scan.Total, currency);
        if (Minor(scan.Tip, currency) is { } tip && tip != 0 && !adjustments.Any(a => a.Kind == AdjustmentKind.Tip))
            adjustments.Add(new AdjustmentDraft(AdjustmentKind.Tip, "Tip", tip));
        if (Minor(scan.Tax, currency) is { } tax && tax != 0 && !adjustments.Any(a => a.Kind == AdjustmentKind.Tax))
        {
            var onTop = subtotal is { } s && printedTotal is { } t && s + tax + (Minor(scan.Tip, currency) ?? 0) == t;
            adjustments.Add(new AdjustmentDraft(AdjustmentKind.Tax, "Tax", tax, IncludedInPrices: !onTop));
        }

        var merchant = Clean(scan.Merchant);
        return new ReceiptDraft(
            Label: merchant ?? "Receipt",
            ConfirmedTotal: printedTotal ?? 0,
            Lines: lines,
            Adjustments: adjustments,
            Merchant: merchant,
            ReceiptDate: scan.Date);
    }

    /// <summary>A second photo of the same long receipt: its lines and fees follow the first's. The later total wins.</summary>
    public static ReceiptDraft Append(ReceiptDraft first, ReceiptDraft next) => first with
    {
        Lines = [.. first.Lines, .. next.Lines],
        Adjustments = [.. first.Adjustments ?? [], .. next.Adjustments ?? []],
        ConfirmedTotal = next.ConfirmedTotal != 0 ? next.ConfirmedTotal : first.ConfirmedTotal,
        Merchant = first.Merchant ?? next.Merchant,
        ReceiptDate = first.ReceiptDate ?? next.ReceiptDate,
    };

    public static AdjustmentKind? Classify(string text, long amount)
    {
        var t = text.ToLowerInvariant();
        if (SeniorWords().IsMatch(t)) return AdjustmentKind.Senior;
        if (PwdWords().IsMatch(t)) return AdjustmentKind.Pwd;
        if (VatExemptWords().IsMatch(t)) return AdjustmentKind.VatExempt;
        if (ServiceWords().IsMatch(t)) return AdjustmentKind.Service;
        if (amount < 0 || DiscountWords().IsMatch(t)) return AdjustmentKind.Discount;
        if (TipWords().IsMatch(t)) return AdjustmentKind.Tip;
        if (TaxWords().IsMatch(t)) return AdjustmentKind.Tax;
        return null;
    }

    /// <summary>"40% BDO OFF" → 4000 basis points.</summary>
    public static int? RateIn(string text)
    {
        var match = PercentRegex().Match(text);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent) && percent is > 0 and <= 100
            ? (int)decimal.Round(percent * 100)
            : null;
    }

    /// <summary>Printed amount to whole minor units, rounded half away from zero to the currency's digits.</summary>
    public static long? Minor(decimal? amount, CurrencyInfo currency)
    {
        if (amount is not { } a)
            return null;
        var scaled = decimal.Round(a * Pow10(currency.Digits), MidpointRounding.AwayFromZero);
        return Math.Abs(scaled) > ReceiptDrafts.MaxAmount ? null : (long)scaled;
    }

    private static decimal Pow10(int digits) => digits switch { 0 => 1m, 1 => 10m, 2 => 100m, 3 => 1000m, _ => 10000m };

    private static string? Clean(string? text) =>
        text is null ? null : Text.Clean(MultiSpace().Replace(text.ReplaceLineEndings(" "), " "), Text.MaxLabel);

    [GeneratedRegex(@"\b(sc|senior)\b.*\b(disc|discount)\b|\bsenior\b|\bsc\s*disc")] private static partial Regex SeniorWords();
    [GeneratedRegex(@"\bpwd\b")] private static partial Regex PwdWords();
    [GeneratedRegex(@"vat[\s-]*exempt|less\s*(:\s*)?vat|\bvat\s*ex\b")] private static partial Regex VatExemptWords();
    [GeneratedRegex(@"service\s*(charge|chg|fee)|\bsvc\b|\bs/c\b|\bserv\b")] private static partial Regex ServiceWords();
    [GeneratedRegex(@"discount|\bdisc\b|%\s*off|\boff\b|\bless\b|promo|voucher|coupon")] private static partial Regex DiscountWords();
    [GeneratedRegex(@"\btip\b|gratuity")] private static partial Regex TipWords();
    [GeneratedRegex(@"\bvat\b|\btax\b")] private static partial Regex TaxWords();
    [GeneratedRegex(@"\bvat\b", RegexOptions.IgnoreCase)] private static partial Regex VatWords();
    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,2})?)\s*%")] private static partial Regex PercentRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex MultiSpace();
}
