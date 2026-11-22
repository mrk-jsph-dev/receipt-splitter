namespace ReceiptSplitter.Core.Shared;

// Bill data as the maths engine sees it.
// All money is in minor units (long). Weights and shares are whole numbers; the API scales
// fractional shares (a kid at 0.5) to integers before calling Core.

public enum Card { None, Senior, Pwd }

public enum SplitMode { Items, Equal, Shares }

public enum AdjustmentKind { Tax, Tip, Service, Discount, Fee, Senior, Pwd, VatExempt }

public enum AdjustmentSplit { Proportional, Equal }

/// <summary>People order (the order of the bill's people list) breaks every tie.</summary>
public sealed record Person(string Id, string Name, Card Card = Card.None);

public sealed record Claim(string PersonId, long Weight = 1);

public sealed record Unit(string Id, IReadOnlyList<Claim> Claims);

/// <summary>A printed line. <see cref="LineTotal"/> is the truth and is spread over the units.</summary>
public sealed record Line(string Id, string Name, int Quantity, long UnitPrice, long LineTotal, IReadOnlyList<Unit> Units);

/// <summary>Limits an adjustment to some units, some people, or both. Null or empty means not limited.</summary>
public sealed record AdjustmentScope(IReadOnlyList<string>? Units = null, IReadOnlyList<string>? People = null);

/// <summary>A fee or discount on one receipt. <see cref="Amount"/> is signed: discounts are negative.</summary>
public sealed record Adjustment(
    string Id,
    AdjustmentKind Kind,
    string Label,
    long Amount,
    AdjustmentSplit Split = AdjustmentSplit.Proportional,
    bool IncludedInPrices = false,
    AdjustmentScope? Scope = null)
{
    public bool IsPersonScoped => Scope?.People is { Count: > 0 };
}

public sealed record Payer(string PersonId, long Amount);

/// <param name="Participants">Who was there. Empty means everyone on the bill.</param>
/// <param name="Shares">Split mode "shares": weight per person. Participants missing from it count as 1.</param>
public sealed record Receipt(
    string Id,
    string Label,
    long ConfirmedTotal,
    IReadOnlyList<Line> Lines,
    IReadOnlyList<Adjustment> Adjustments,
    IReadOnlyList<string> Participants,
    IReadOnlyList<Payer> Payers,
    SplitMode SplitMode = SplitMode.Items,
    IReadOnlyDictionary<string, long>? Shares = null);
