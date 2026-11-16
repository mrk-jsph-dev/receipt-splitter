using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Receipts;

public class SplitModeTests
{
    [Fact]
    public void Equal_and_shares_sum_exactly_for_one_to_fifty_people()
    {
        for (var n = 1; n <= 50; n++)
        {
            var people = Enumerable.Range(1, n).Select(i => new Person($"p{i}", $"P{i}")).ToList();
            var ids = people.Select(p => p.Id).ToList();
            var shares = ids.Select((id, i) => (id, weight: (long)(i % 3 + 1))).ToDictionary(x => x.id, x => x.weight);
            var lines = new[] { Line("x", 1, 525581) };

            var equal = ReceiptCalculator.Calculate(people, Receipt(525581, lines, participants: ids, splitMode: SplitMode.Equal));
            var byShares = ReceiptCalculator.Calculate(people, Receipt(525581, lines, participants: ids, splitMode: SplitMode.Shares, shares: shares));

            Assert.Equal(525581, equal.People.Sum(p => p.Total));
            Assert.Equal(525581, byShares.People.Sum(p => p.Total));
            Assert.InRange(equal.People.Max(p => p.Total) - equal.People.Min(p => p.Total), 0, 1);
        }
    }

    [Fact]
    public void Equal_mode_ignores_claims_but_keeps_them()
    {
        var dinner = SampleBill.Dinner(SplitMode.Equal);

        var result = ReceiptCalculator.Calculate(SampleBill.People, dinner);

        Assert.Equal(Allocation.Allocate(525580, Enumerable.Repeat(1L, 12).ToList()), result.People.Select(p => p.Total));
        Assert.Equal(0, result.Unclaimed.Total);

        var backToItems = ReceiptCalculator.Calculate(SampleBill.People, dinner with { SplitMode = SplitMode.Items });
        Assert.Equal(38574, backToItems.TotalOf("ben"));
    }

    [Fact]
    public void Equal_mode_splits_only_among_participants()
    {
        var people = People("a", "b", "c", "d");
        var receipt = Receipt(900, [Line("x", 1, 900)], participants: ["b", "d", "c"], splitMode: SplitMode.Equal);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([0L, 300, 300, 300], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Shares_mode_counts_a_couple_as_two()
    {
        var people = People("a", "b", "c");
        var receipt = Receipt(1000, [Line("x", 1, 1000)], splitMode: SplitMode.Shares,
            shares: new Dictionary<string, long> { ["a"] = 2, ["b"] = 1, ["c"] = 1 });

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([500L, 250, 250], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Equal_mode_splits_the_confirmed_total_even_when_lines_do_not_match()
    {
        var people = People("a", "b");
        var result = ReceiptCalculator.Calculate(people, Receipt(1001, [Line("x", 1, 900)], splitMode: SplitMode.Equal));

        Assert.False(result.Matches);
        Assert.Equal([501L, 500], result.People.Select(p => p.Total));
    }
}
