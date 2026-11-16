namespace ReceiptSplitter.Core.Tests.Shared;

public class AllocationTests
{
    [Fact]
    public void Splits_exactly_when_it_divides()
    {
        Assert.Equal([2500L, 2500, 5000], Allocation.Allocate(10000, [1, 1, 2]));
    }

    [Fact]
    public void Leftover_goes_to_largest_remainder()
    {
        // 10001 × 2/4 = 5000.5, the others 2500.25: the half wins the extra unit.
        Assert.Equal([5001L, 2500, 2500], Allocation.Allocate(10001, [2, 1, 1]));
    }

    [Fact]
    public void Ties_go_to_the_earlier_person()
    {
        Assert.Equal([3334L, 3333, 3333], Allocation.Allocate(10000, [1, 1, 1]));
        Assert.Equal([5674L, 5674, 5674, 5674, 5673, 5673, 5673, 5673, 5673, 5673, 5673, 5673],
            Allocation.Allocate(68080, Enumerable.Repeat(1L, 12).ToList()));
    }

    [Fact]
    public void Negative_totals_are_allocated_as_positive_then_negated()
    {
        Assert.Equal([-3334L, -3333, -3333], Allocation.Allocate(-10000, [1, 1, 1]));
    }

    [Fact]
    public void Zero_weights_get_nothing()
    {
        Assert.Equal([0L, 50, 0, 50], Allocation.Allocate(100, [0, 1, 0, 1]));
        Assert.Equal([0L, 1, 0], Allocation.Allocate(1, [0, 5, 0]));
    }

    [Fact]
    public void Zero_total_gives_zeros()
    {
        Assert.Equal([0L, 0], Allocation.Allocate(0, [3, 7]));
    }

    [Fact]
    public void Rejects_bad_weights()
    {
        Assert.Throws<ArgumentException>(() => Allocation.Allocate(100, []));
        Assert.Throws<ArgumentException>(() => Allocation.Allocate(100, [0, 0]));
        Assert.Throws<ArgumentException>(() => Allocation.Allocate(100, [1, -1]));
    }

    [Fact]
    public void Random_cases_always_sum_exactly_and_stay_within_one_unit_of_exact()
    {
        var random = new Random(20260925);
        for (var run = 0; run < 10_000; run++)
        {
            var count = random.Next(1, 60);
            var weights = Enumerable.Range(0, count).Select(_ => random.NextInt64(0, 1_000_000)).ToArray();
            if (weights.All(w => w == 0)) weights[0] = 1;
            var total = random.NextInt64(-1_000_000_000, 1_000_000_000);

            var shares = Allocation.Allocate(total, weights);

            Assert.Equal(total, shares.Sum());
            var weightSum = weights.Sum();
            for (var i = 0; i < count; i++)
            {
                // Each share is the exact value rounded down or up by at most one minor unit.
                var floor = Math.Abs(total) * weights[i] / weightSum;
                Assert.InRange(Math.Abs(shares[i]), floor, floor + 1);
                Assert.True(shares[i] == 0 || Math.Sign(shares[i]) == Math.Sign(total));
            }
        }
    }
}
