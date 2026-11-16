using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Receipts;

/// <summary>Each claimed unit's price per person, so a breakdown can say "⅓ of Classic Gyoza: 63.34".</summary>
public class UnitSharesTests
{
    [Fact]
    public void Shared_gyoza_lists_each_claimant_share_and_they_sum_to_the_price()
    {
        var result = ReceiptCalculator.Calculate(SampleBill.People, SampleBill.Dinner());

        var gyoza = result.Items.Where(s => s.UnitId == "gyoza-1").ToList();
        Assert.Equal([new UnitShare("gyoza-1", "ben", 6334), new UnitShare("gyoza-1", "fay", 6333), new UnitShare("gyoza-1", "gio", 6333)], gyoza);
        Assert.Contains(new UnitShare("butao-2", "ben", 48500), result.Items);
    }

    [Fact]
    public void Unclaimed_units_are_listed_with_no_person_and_every_unit_sums_to_its_line()
    {
        var receipt = Receipt(30000, [Line("deal", 3, 10000, 3333, By("ana"))]);

        var result = ReceiptCalculator.Calculate(People("ana", "ben"), receipt);

        Assert.Equal(
            [new UnitShare("deal-1", "ana", 3334), new UnitShare("deal-2", null, 3333), new UnitShare("deal-3", null, 3333)],
            result.Items);
        Assert.Equal(10000, result.Items.Sum(i => i.Amount));
    }

    [Fact]
    public void Equal_and_shares_modes_list_no_items()
    {
        var result = ReceiptCalculator.Calculate(SampleBill.People, SampleBill.Dinner(SplitMode.Equal));

        Assert.Empty(result.Items);
    }
}
