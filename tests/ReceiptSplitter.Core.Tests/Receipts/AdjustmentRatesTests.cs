using ReceiptSplitter.Core.Receipts;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Core.Tests.Receipts;

public class AdjustmentRatesTests
{
    private static Line LineOf(string id, int quantity, long total) =>
        new(id, id, quantity, total / quantity, total, Enumerable.Range(1, quantity).Select(n => new Unit($"{id}-{n}", [])).ToList());

    // Ramen Nagi: 7,625.00 of food. 40% off is −3,050.00, a 10% service charge would be 762.50.
    private static readonly IReadOnlyList<Line> Dinner = [LineOf("butao", 8, 388000), LineOf("rest", 1, 374500)];

    [Fact]
    public void Forty_percent_off_the_dinner_is_minus_3050()
    {
        Assert.Equal(-305000, AdjustmentRates.Suggest(Dinner, AdjustmentKind.Discount, 4000));
        Assert.Equal(76250, AdjustmentRates.Suggest(Dinner, AdjustmentKind.Service, 1000));
    }

    [Fact]
    public void Rounds_half_away_from_zero()
    {
        IReadOnlyList<Line> lines = [LineOf("a", 1, 105)];
        Assert.Equal(-53, AdjustmentRates.Suggest(lines, AdjustmentKind.Senior, 5000));
        Assert.Equal(11, AdjustmentRates.Suggest(lines, AdjustmentKind.Tax, 1000));
    }

    [Fact]
    public void Some_items_counts_only_those_units()
    {
        // Two of the eight Butao King bowls, 485.00 each: 20% is 194.00.
        Assert.Equal(-19400, AdjustmentRates.Suggest(Dinner, AdjustmentKind.Discount, 2000, ["butao-1", "butao-2"]));
    }
}
