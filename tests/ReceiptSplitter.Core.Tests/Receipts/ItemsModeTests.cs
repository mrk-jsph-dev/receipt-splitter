using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Receipts;

public class ItemsModeTests
{
    [Fact]
    public void Sample_receipt_reproduces_the_published_numbers()
    {
        var receipt = SampleBill.Dinner();

        var result = ReceiptCalculator.Calculate(SampleBill.People, receipt);

        Assert.Equal(20, receipt.Lines.Sum(l => l.Units.Count));
        Assert.True(result.Matches);
        Assert.Equal(525580, result.ComputedTotal);
        Assert.Equal(0, result.Unclaimed.Total);
        Assert.Equal(525580, result.People.Sum(p => p.Total));

        var service = result.People.Select(p => p.Adjustments["service"]).ToList();
        Assert.Equal(4, service.Count(s => s == 5674));
        Assert.Equal(8, service.Count(s => s == 5673));

        var ben = result.People.Single(p => p.PersonId == "ben");
        Assert.Equal(54834, ben.Subtotal);
        Assert.Equal(-21934, ben.Adjustments["bdo"]);
        Assert.Equal(5674, ben.Adjustments["service"]);
        Assert.Equal(38574, ben.Total);

        Assert.Equal(40474, result.TotalOf("ana"));
        Assert.Equal(53374, result.TotalOf("carla"));
        Assert.Equal(48274, result.TotalOf("dan"));
        Assert.Equal(34773, result.TotalOf("eli"));
    }

    [Fact]
    public void Shared_gyoza_gives_the_extra_centavo_to_the_earliest_person()
    {
        var result = ReceiptCalculator.Calculate(SampleBill.People, SampleBill.Dinner());

        Assert.Equal(48500 + 6334, result.People.Single(p => p.PersonId == "ben").Subtotal);
        Assert.Equal(48500 + 6333, result.People.Single(p => p.PersonId == "fay").Subtotal);
    }

    [Fact]
    public void Three_for_one_hundred_spreads_the_odd_centavo()
    {
        var people = People("a", "b", "c");
        var line = Line("bun", quantity: 3, lineTotal: 10000, unitPrice: 3333, By("c"), By("b"), By("a"));

        var result = ReceiptCalculator.Calculate(people, Receipt(10000, [line]));

        // Unit 1 carries the extra centavo, whoever claimed it.
        Assert.Equal(3333, result.TotalOf("a"));
        Assert.Equal(3333, result.TotalOf("b"));
        Assert.Equal(3334, result.TotalOf("c"));
    }

    [Fact]
    public void Shared_unit_with_weights_two_one_one()
    {
        var people = People("a", "b", "c");
        var line = Line("platter", 1, 10001, [new Claim("a", 2), new Claim("b", 1), new Claim("c", 1)]);

        var result = ReceiptCalculator.Calculate(people, Receipt(10001, [line]));

        Assert.Equal(5001, result.TotalOf("a"));
        Assert.Equal(2500, result.TotalOf("b"));
        Assert.Equal(2500, result.TotalOf("c"));
    }

    [Fact]
    public void Tax_tip_and_discount_reconcile_with_unclaimed_items()
    {
        var people = People("a", "b");
        var receipt = Receipt(
            confirmedTotal: 6100,
            lines: [Line("x", 1, 1000, By("a")), Line("y", 1, 2000, By("b")), Line("z", 1, 3000)],
            adjustments:
            [
                new Adjustment("tax", AdjustmentKind.Tax, "Tax", 600),
                new Adjustment("tip", AdjustmentKind.Tip, "Tip", 500, AdjustmentSplit.Equal),
                new Adjustment("promo", AdjustmentKind.Discount, "Promo", -1000),
            ]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.True(result.Matches);
        Assert.Equal(1000 + 100 + 250 - 167, result.TotalOf("a"));
        Assert.Equal(2000 + 200 + 250 - 333, result.TotalOf("b"));
        Assert.Equal(3000 + 300 - 500, result.Unclaimed.Total);
        Assert.Equal(0, result.Unclaimed.Adjustments["tip"]);
        Assert.Equal(6100, result.People.Sum(p => p.Total) + result.Unclaimed.Total);
    }

    [Fact]
    public void Tax_included_in_prices_adds_nothing()
    {
        var people = People("a");
        var receipt = Receipt(11200, [Line("x", 1, 11200, By("a"))],
            [new Adjustment("vat", AdjustmentKind.Tax, "VAT 12%", 1200, IncludedInPrices: true)]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.True(result.Matches);
        Assert.Equal(11200, result.TotalOf("a"));
        Assert.False(result.People[0].Adjustments.ContainsKey("vat"));
    }

    [Fact]
    public void Equal_fee_charges_participants_with_no_items()
    {
        var people = People("a", "b", "c");
        var receipt = Receipt(1300, [Line("x", 1, 1000, By("a"))],
            [new Adjustment("svc", AdjustmentKind.Service, "Service", 300, AdjustmentSplit.Equal)]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([1100L, 100, 100], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Unit_scoped_discount_applies_only_to_those_units()
    {
        var people = People("a", "b");
        var receipt = Receipt(
            confirmedTotal: 2500,
            lines: [Line("wine", 1, 1000, By("a", "b")), Line("steak", 1, 2000, By("b"))],
            adjustments:
            [
                new Adjustment("happy-hour", AdjustmentKind.Discount, "Wine 50% off", -500,
                    Scope: new AdjustmentScope(Units: ["wine-1"])),
            ]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal(500 - 250, result.TotalOf("a"));
        Assert.Equal(500 + 2000 - 250, result.TotalOf("b"));
    }

    [Fact]
    public void Mismatch_with_the_printed_total_is_reported_not_hidden()
    {
        var result = ReceiptCalculator.Calculate(People("a"), Receipt(1500, [Line("x", 1, 1000, By("a"))]));

        Assert.False(result.Matches);
        Assert.Equal(-500, result.Difference);
        Assert.Equal(1000, result.TotalOf("a"));
    }

    [Fact]
    public void Same_numbers_whatever_order_claims_were_made_in()
    {
        var expected = ReceiptCalculator.Calculate(SampleBill.People, SampleBill.Dinner());
        var random = new Random(7);

        for (var run = 0; run < 20; run++)
        {
            var dinner = SampleBill.Dinner();
            var shuffled = dinner with
            {
                Lines = dinner.Lines.Select(line => line with
                {
                    Units = line.Units.Select(u => u with { Claims = u.Claims.OrderBy(_ => random.Next()).ToList() }).ToList(),
                }).ToList(),
            };

            var result = ReceiptCalculator.Calculate(SampleBill.People, shuffled);

            Assert.Equal(expected.People.Select(p => p.Total), result.People.Select(p => p.Total));
        }
    }

    [Fact]
    public void Discount_with_no_claims_in_scope_falls_back_to_equal_and_warns()
    {
        var people = People("a", "b", "c");
        var receipt = Receipt(1800, [Line("x", 1, 2000, By("a"))],
            [new Adjustment("sc", AdjustmentKind.Senior, "SC DISC", -200,
                Scope: new AdjustmentScope(People: ["b", "c"]))]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([2000L, -100, -100], result.People.Select(p => p.Total));
        Assert.Contains(result.Warnings, w => w.AdjustmentId == "sc");
    }

    [Fact]
    public void Rejects_claims_by_unknown_people()
    {
        var receipt = Receipt(1000, [Line("x", 1, 1000, By("stranger"))]);

        Assert.Throws<ArgumentException>(() => ReceiptCalculator.Calculate(People("a"), receipt));
    }
}
