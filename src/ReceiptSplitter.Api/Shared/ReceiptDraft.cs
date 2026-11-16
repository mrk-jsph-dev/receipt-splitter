using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Shared;

/// <summary>
/// A receipt as typed in, read by a scan or loaded from the sample, and the body of a receipt
/// replace. The server adds ids and units. Ids, scopes, participants, payers and shares refer to
/// things already on the bill, so they are only useful when editing a receipt.
/// </summary>
/// <param name="Participants">Who was there. Empty or missing means everyone.</param>
/// <param name="Payers">Who paid this receipt and how much.</param>
/// <param name="Shares">Split mode "shares": weight per person, up to 2 decimals (a kid as 0.5). Missing counts as 1.</param>
public sealed record ReceiptDraft(
    string Label,
    long ConfirmedTotal,
    IReadOnlyList<LineDraft> Lines,
    IReadOnlyList<AdjustmentDraft>? Adjustments = null,
    string? Merchant = null,
    DateOnly? ReceiptDate = null,
    SplitMode SplitMode = SplitMode.Items,
    IReadOnlyList<string>? Participants = null,
    IReadOnlyList<PayerDoc>? Payers = null,
    IReadOnlyDictionary<string, decimal>? Shares = null);

/// <param name="LineTotal">As printed. Defaults to quantity × unit price; differs for deals like 3 for 100.00.</param>
/// <param name="Id">An existing line's id. Its units keep their claims, up to the new quantity.</param>
public sealed record LineDraft(
    string Name,
    int Quantity,
    long UnitPrice,
    long? LineTotal = null,
    decimal? Confidence = null,
    string? Id = null);

/// <param name="Amount">Signed minor units as printed: discounts are negative.</param>
/// <param name="Rate">Percent helper in basis points (4000 = 40%). The amount is the truth.</param>
/// <param name="Scope">Limits it to some units of this receipt or some people. Senior and PWD go to their card holders.</param>
public sealed record AdjustmentDraft(
    AdjustmentKind Kind,
    string Label,
    long Amount,
    int? Rate = null,
    bool IncludedInPrices = false,
    AdjustmentSplit Split = AdjustmentSplit.Proportional,
    ScopeDoc? Scope = null,
    string? Id = null);

/// <summary>A claim that went away because its unit did: the line was deleted or its quantity cut.</summary>
public sealed record DroppedClaim(string PersonId, string UnitId, string LineName);

public static class Text
{
    public const int MaxPersonName = 40;
    public const int MaxLabel = 80;
    public const int MaxPaymentNote = 280;

    /// <summary>Trimmed text of 1 to <paramref name="max"/> characters with no control characters, or null.</summary>
    public static string? Clean(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > max || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}

public static class ReceiptDrafts
{
    /// <summary>
    /// Largest line total or adjustment, in minor units. Core multiplies amounts by weights in
    /// checked 64-bit maths, and this bound keeps a full receipt far from overflow.
    /// </summary>
    public const long MaxAmount = 100_000_000;

    public const decimal MaxShare = 100;

    /// <summary>
    /// Checks a draft against this bill and builds the stored receipt. With <paramref name="existing"/>
    /// it replaces that receipt: its id and source stay, and units that still exist keep their claims.
    /// </summary>
    public static (ReceiptDoc? Receipt, List<DroppedClaim> Dropped, OpError? Error) ToDocument(
        ReceiptDraft draft, ReceiptSource source, BillLimits limits, IReadOnlyList<PersonDoc> people, ReceiptDoc? existing = null)
    {
        var dropped = new List<DroppedClaim>();
        (ReceiptDoc?, List<DroppedClaim>, OpError?) Fail(string reason) => (null, dropped, OpError.Invalid(reason));
        var personIds = people.Select(p => p.Id).ToHashSet();

        var label = Text.Clean(draft.Label, Text.MaxLabel);
        if (label is null)
            return Fail("receipt-label");
        string? merchant = null;
        if (draft.Merchant is not null && (merchant = Text.Clean(draft.Merchant, Text.MaxLabel)) is null)
            return Fail("merchant");
        if (draft.Lines.Count > limits.MaxLinesPerReceipt)
            return Fail("too-many-lines");
        var adjustments = draft.Adjustments ?? [];
        if (adjustments.Count > limits.MaxLinesPerReceipt)
            return Fail("too-many-adjustments");
        var maxTotal = MaxAmount * limits.MaxLinesPerReceipt;
        if (Math.Abs(draft.ConfirmedTotal) > maxTotal)
            return Fail("confirmed-total");

        var oldLines = existing?.Lines.ToDictionary(l => l.Id) ?? [];
        var lineIds = new HashSet<string>();
        var lines = new List<LineDoc>(draft.Lines.Count);
        foreach (var line in draft.Lines)
        {
            var name = Text.Clean(line.Name, Text.MaxLabel);
            if (name is null)
                return Fail("line-name");
            if (line.Quantity < 1 || line.Quantity > limits.MaxUnitsPerLine)
                return Fail("line-quantity");
            if (Math.Abs(line.UnitPrice) > MaxAmount)
                return Fail("line-price");
            var lineTotal = line.LineTotal ?? line.Quantity * line.UnitPrice;
            if (Math.Abs(lineTotal) > MaxAmount)
                return Fail("line-total");
            if (line.Confidence is < 0 or > 1)
                return Fail("line-confidence");
            LineDoc? old = null;
            if (line.Id is not null && !oldLines.TryGetValue(line.Id, out old))
                return Fail("unknown-line");
            if (line.Id is not null && !lineIds.Add(line.Id))
                return Fail("duplicate-line");

            var id = old?.Id ?? SecureIds.NewShort();
            var oldUnits = old?.Units.ToDictionary(u => u.Id) ?? [];
            lines.Add(new LineDoc
            {
                Id = id,
                Name = name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = lineTotal,
                Confidence = line.Confidence,
                Units = Enumerable.Range(1, line.Quantity)
                    .Select(n => new UnitDoc { Id = $"{id}-{n}", Claims = oldUnits.GetValueOrDefault($"{id}-{n}")?.Claims ?? [] })
                    .ToList(),
            });
        }

        // Claims on units that no longer exist go with them, and the people affected get a notice.
        var keptUnits = lines.SelectMany(l => l.Units).Select(u => u.Id).ToHashSet();
        foreach (var line in existing?.Lines ?? [])
            foreach (var unit in line.Units.Where(u => !keptUnits.Contains(u.Id)))
                dropped.AddRange(unit.Claims.Select(c => new DroppedClaim(c.PersonId, unit.Id, line.Name)));

        var oldAdjustments = existing?.Adjustments.ToDictionary(a => a.Id) ?? [];
        var adjustmentIds = new HashSet<string>();
        var adjustmentDocs = new List<AdjustmentDoc>(adjustments.Count);
        foreach (var adjustment in adjustments)
        {
            var adjustmentLabel = Text.Clean(adjustment.Label, Text.MaxLabel);
            if (adjustmentLabel is null)
                return Fail("adjustment-label");
            if (Math.Abs(adjustment.Amount) > MaxAmount)
                return Fail("adjustment-amount");
            if (adjustment.Rate is < 0 or > 10_000)
                return Fail("adjustment-rate");
            AdjustmentDoc? old = null;
            if (adjustment.Id is not null && (!oldAdjustments.TryGetValue(adjustment.Id, out old) || !adjustmentIds.Add(adjustment.Id)))
                return Fail("unknown-adjustment");

            var scopeUnits = adjustment.Scope?.Units is { Count: > 0 } u ? u.Distinct().ToList() : null;
            var scopePeople = adjustment.Scope?.People is { Count: > 0 } p ? p.Distinct().ToList() : null;
            if (scopeUnits?.Any(id => !keptUnits.Contains(id)) == true)
                return Fail("unknown-unit");
            if (scopePeople?.Any(id => !personIds.Contains(id)) == true)
                return Fail("unknown-person");

            adjustmentDocs.Add(new AdjustmentDoc
            {
                Id = old?.Id ?? SecureIds.NewShort(),
                Kind = adjustment.Kind,
                Label = adjustmentLabel,
                Amount = adjustment.Amount,
                Rate = adjustment.Rate,
                IncludedInPrices = adjustment.IncludedInPrices,
                Split = adjustment.Split,
                Scope = scopeUnits is null && scopePeople is null ? null : new ScopeDoc(scopeUnits, scopePeople),
                // A scanned fee stays flagged until the organizer saves the receipt, which counts as reviewing it.
                Source = existing is null && source == ReceiptSource.Scan ? AdjustmentSource.Scan : AdjustmentSource.Manual,
            });
        }

        var participants = draft.Participants ?? [];
        if (participants.Any(id => !personIds.Contains(id)))
            return Fail("unknown-person");
        if (participants.Distinct().Count() != participants.Count)
            return Fail("duplicate-participant");

        var payers = draft.Payers ?? [];
        if (payers.Any(p => !personIds.Contains(p.PersonId)))
            return Fail("unknown-person");
        if (payers.Select(p => p.PersonId).Distinct().Count() != payers.Count)
            return Fail("duplicate-payer");
        if (payers.Any(p => p.Amount < 0 || p.Amount > maxTotal))
            return Fail("payer-amount");

        if (draft.Shares?.Keys.Any(id => !personIds.Contains(id)) == true)
            return Fail("unknown-person");
        if (draft.Shares?.Values.Any(v => v <= 0 || v > MaxShare || v * CoreMapping.ShareScale != decimal.Truncate(v * CoreMapping.ShareScale)) == true)
            return Fail("share");

        return (new ReceiptDoc
        {
            Id = existing?.Id ?? SecureIds.NewShort(),
            Label = label,
            Merchant = merchant,
            ReceiptDate = draft.ReceiptDate,
            Source = existing?.Source ?? source,
            Photos = existing?.Photos ?? 0,
            ConfirmedTotal = draft.ConfirmedTotal,
            Lines = lines,
            Adjustments = adjustmentDocs,
            SplitMode = draft.SplitMode,
            Shares = draft.Shares?.ToDictionary(),
            Participants = participants.ToList(),
            Payers = payers.ToList(),
        }, dropped, null);
    }
}
