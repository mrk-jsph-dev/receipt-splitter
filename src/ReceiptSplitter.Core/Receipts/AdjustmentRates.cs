using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Core.Receipts;

/// <summary>
/// "40%" as an amount, for the fees and discounts editor. Only a suggestion: the printed amount is
/// the truth and the organizer confirms it. Rates are basis points (4000 = 40%).
/// </summary>
public static class AdjustmentRates
{
    /// <summary>Discounts and card-holder lines come off the bill; everything else adds to it.</summary>
    public static bool Reduces(AdjustmentKind kind) =>
        kind is AdjustmentKind.Discount or AdjustmentKind.Senior or AdjustmentKind.Pwd or AdjustmentKind.VatExempt;

    /// <summary>
    /// Rate × the items it covers, rounded half away from zero, signed by kind. The base is every
    /// line, or only the units in <paramref name="scopeUnits"/> (each unit's price is its line total
    /// spread evenly over the quantity, the same way the calculator does).
    /// </summary>
    public static long Suggest(IReadOnlyList<Line> lines, AdjustmentKind kind, int rate, IReadOnlyCollection<string>? scopeUnits = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(rate);

        long itemsBase = 0;
        foreach (var line in lines)
        {
            if (scopeUnits is not { Count: > 0 })
            {
                itemsBase = checked(itemsBase + line.LineTotal);
                continue;
            }
            var prices = Allocation.Evenly(line.LineTotal, line.Units.Count);
            for (var i = 0; i < line.Units.Count; i++)
                if (scopeUnits.Contains(line.Units[i].Id))
                    itemsBase = checked(itemsBase + prices[i]);
        }

        var product = checked(Math.Abs(itemsBase) * rate);
        var amount = (product + 5_000) / 10_000;
        if (itemsBase < 0)
            amount = -amount;
        return Reduces(kind) ? -amount : amount;
    }
}
