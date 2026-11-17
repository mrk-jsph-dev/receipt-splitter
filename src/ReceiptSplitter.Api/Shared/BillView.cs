using ReceiptSplitter.Core.Bills;
using ReceiptSplitter.Core.Settlement;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Shared;

/// <summary>
/// What a link holder sees. A separate type from <see cref="BillDocument"/> so the organizer key
/// hash, or any server field added later, can never reach a response by accident.
/// </summary>
public sealed record BillView(
    string Id,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string Title,
    CurrencyInfo Currency,
    BillStatus Status,
    IReadOnlyList<PersonDoc> People,
    IReadOnlyList<ReceiptDoc> Receipts,
    IReadOnlyList<PaymentDoc> Payments,
    IReadOnlyList<ActivityDoc> Activity,
    int? HeadcountHint = null,
    string? PaymentNote = null,
    DateTimeOffset? LockedAt = null,
    bool HasPin = false);

/// <summary>
/// The server's maths, straight from Core. The browser shows these numbers and never works out money.
/// Null when the bill has no people yet, since there is no one to split between.
/// </summary>
public sealed record BreakdownView(
    bool CanShare,
    bool CanSettle,
    IReadOnlyList<Core.Receipts.ReceiptBreakdown> Receipts,
    IReadOnlyList<PersonBalance> People,
    IReadOnlyList<BillProblem> Problems,
    IReadOnlyList<Transfer> Transfers,
    BillProgress Progress);

/// <summary>
/// Headline numbers for lists and headers, worked out here so the browser never adds money up.
/// </summary>
/// <param name="Total">Sum of every receipt's printed total.</param>
/// <param name="Owing">People who owe money overall (net below zero).</param>
/// <param name="Confirmed">Of those, how many the organizer has confirmed as paid.</param>
/// <param name="PaidPercent">Confirmed out of owing, 0 to 100, rounded down. 0 when nobody owes yet.</param>
/// <param name="Collected">What the confirmed people owed in total.</param>
/// <param name="ToCollect">What everyone not yet confirmed still owes.</param>
public sealed record BillProgress(long Total, int Owing, int Confirmed, int PaidPercent, long Collected, long ToCollect)
{
    public static BillProgress From(BillDocument doc, IReadOnlyList<PersonBalance> people)
    {
        var total = doc.Receipts.Sum(r => r.ConfirmedTotal);
        var confirmed = doc.Payments.Where(p => p.Status == PaymentStatus.Confirmed).Select(p => p.PersonId).ToHashSet();
        var owing = people.Where(p => p.Net < 0).ToList();
        var paid = owing.Count(p => confirmed.Contains(p.PersonId));
        var collected = owing.Where(p => confirmed.Contains(p.PersonId)).Sum(p => -p.Net);
        var toCollect = owing.Where(p => !confirmed.Contains(p.PersonId)).Sum(p => -p.Net);
        return new BillProgress(total, owing.Count, paid, owing.Count == 0 ? 0 : paid * 100 / owing.Count, collected, toCollect);
    }
}

public sealed record BillResponse(BillView Bill, BreakdownView? Breakdown = null)
{
    public static BillResponse From(BillDocument doc, int retentionDays)
    {
        var view = new BillView(
            doc.Id, doc.Version, doc.CreatedAt, doc.UpdatedAt, doc.UpdatedAt.AddDays(retentionDays),
            doc.Title, doc.Currency, doc.Status, doc.People, doc.Receipts, doc.Payments, doc.Activity,
            doc.HeadcountHint, doc.PaymentNote, doc.LockedAt, doc.PinHash is not null);
        return new BillResponse(view, Calculate(doc));
    }

    /// <summary>Every receipt matches its printed total, and there is someone to split it between.</summary>
    public static bool CanShare(BillDocument doc) => Calculate(doc)?.CanShare == true;

    private static BreakdownView? Calculate(BillDocument doc)
    {
        if (doc.People.Count == 0)
            return null;

        var people = doc.People.Select(CoreMapping.ToCore).ToList();
        var bill = BillCalculator.Calculate(people, doc.Receipts.Select(r => CoreMapping.ToCore(r, doc.People)).ToList());
        var canShare = doc.Receipts.Count > 0 && bill.Problems.All(p => p.Code != ProblemCode.TotalMismatch);
        var transfers = bill.CanSettle ? SettleUp.Transfers(bill) : [];
        return new BreakdownView(canShare, bill.CanSettle, bill.Receipts, bill.People, bill.Problems, transfers, BillProgress.From(doc, bill.People));
    }
}

/// <summary>Stored bill to Core model. Fractional shares are scaled to whole hundredths.</summary>
public static class CoreMapping
{
    public const int ShareScale = 100;

    public static Person ToCore(PersonDoc p) => new(p.Id, p.Name, p.Card);

    public static Receipt ToCore(ReceiptDoc r, IReadOnlyList<PersonDoc> billPeople) => new(
        r.Id,
        r.Label,
        r.ConfirmedTotal,
        r.Lines.Select(l => new Line(
            l.Id, l.Name, l.Quantity, l.UnitPrice, l.LineTotal,
            l.Units.Select(u => new Unit(u.Id, u.Claims.Select(c => new Claim(c.PersonId, c.Weight)).ToList())).ToList()))
            .ToList(),
        r.Adjustments.Select(a => new Adjustment(
            a.Id, a.Kind, a.Label, a.Amount, a.Split, a.IncludedInPrices,
            a.Scope is null ? null : new AdjustmentScope(a.Scope.Units, a.Scope.People)))
            .ToList(),
        r.Participants,
        r.Payers.Select(p => new Payer(p.PersonId, p.Amount)).ToList(),
        r.SplitMode,
        r.SplitMode == SplitMode.Shares ? ScaledShares(r, billPeople) : null);

    // Core counts a missing share as 1, so every participant gets an explicit scaled weight.
    private static Dictionary<string, long> ScaledShares(ReceiptDoc r, IReadOnlyList<PersonDoc> billPeople)
    {
        var who = r.Participants.Count > 0 ? r.Participants : billPeople.Select(p => p.Id).ToList();
        return who.ToDictionary(
            id => id,
            id => decimal.ToInt64((r.Shares?.GetValueOrDefault(id, 1m) ?? 1m) * ShareScale));
    }
}
