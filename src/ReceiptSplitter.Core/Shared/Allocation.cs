namespace ReceiptSplitter.Core.Shared;

public static class Allocation
{
    /// <summary>
    /// Splits <paramref name="total"/> by <paramref name="weights"/> with the largest remainder method.
    /// Each share is floor(total × w / Σw); the leftover units go one each to the largest remainders,
    /// ties to the lower index (earlier in people order). The result always sums to the total.
    /// A negative total is allocated as positive and negated, so discounts round like charges.
    /// </summary>
    public static long[] Allocate(long total, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
            throw new ArgumentException("At least one weight is needed.", nameof(weights));

        long weightSum = 0;
        foreach (var weight in weights)
        {
            if (weight < 0)
                throw new ArgumentException("Weights cannot be negative.", nameof(weights));
            weightSum = checked(weightSum + weight);
        }
        if (weightSum == 0)
            throw new ArgumentException("Weights cannot all be zero.", nameof(weights));

        if (total < 0)
        {
            var positive = Allocate(checked(-total), weights);
            for (var i = 0; i < positive.Length; i++)
                positive[i] = -positive[i];
            return positive;
        }

        var shares = new long[weights.Count];
        var remainders = new long[weights.Count];
        long given = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var product = checked(total * weights[i]);
            shares[i] = product / weightSum;
            remainders[i] = product % weightSum;
            given = checked(given + shares[i]);
        }

        // Fewer leftover units than non-zero remainders, so zero weights never receive one.
        var leftover = total - given;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i);
        foreach (var i in order.Take((int)leftover))
            shares[i]++;

        return shares;
    }

    /// <summary>Equal split: one weight per slot.</summary>
    public static long[] Evenly(long total, int count) =>
        Allocate(total, Enumerable.Repeat(1L, count).ToArray());
}
