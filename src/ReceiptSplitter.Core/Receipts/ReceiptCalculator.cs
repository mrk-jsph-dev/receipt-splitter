using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Core.Receipts;

public enum WarningCode
{
    /// <summary>A proportional adjustment had nothing to weigh by in its scope, so it was split equally.</summary>
    NothingInScope,

    /// <summary>A person's own discounts are larger than what they had. The restaurant may have assumed a group meal.</summary>
    DiscountExceedsClaims,
}

public sealed record ReceiptWarning(WarningCode Code, string? AdjustmentId = null, string? PersonId = null);

/// <summary>
/// One person's part of a receipt, or the Unclaimed bucket when <see cref="PersonId"/> is null.
/// <see cref="Subtotal"/> is the food they claimed in items mode, or their share of the total
/// before person-scoped adjustments in equal and shares modes.
/// </summary>
public sealed record PersonShare(string? PersonId, long Subtotal, IReadOnlyDictionary<string, long> Adjustments)
{
    public long Total => checked(Subtotal + Adjustments.Values.Sum());
}

/// <summary>One person's part of one unit's price, or the unclaimed price when <see cref="PersonId"/> is null.</summary>
public sealed record UnitShare(string UnitId, string? PersonId, long Amount);

/// <param name="People">One entry per bill person, in people order, including people not on this receipt.</param>
/// <param name="ComputedTotal">Σ line totals + Σ adjustments not already included in prices.</param>
/// <param name="Items">Items mode only: each unit's price per claimant, in line, unit and people order.</param>
public sealed record ReceiptBreakdown(
    string ReceiptId,
    IReadOnlyList<PersonShare> People,
    PersonShare Unclaimed,
    long ComputedTotal,
    long ConfirmedTotal,
    IReadOnlyList<ReceiptWarning> Warnings,
    IReadOnlyList<UnitShare> Items)
{
    /// <summary>Computed minus printed. Sharing stays blocked until this is zero.</summary>
    public long Difference => ComputedTotal - ConfirmedTotal;
    public bool Matches => Difference == 0;
}

/// <summary>The maths for one receipt. Nothing here reads another receipt. See docs/SPEC.md steps 1 to 10.</summary>
public static class ReceiptCalculator
{
    public static ReceiptBreakdown Calculate(IReadOnlyList<Person> people, Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(receipt);

        var ctx = new Context(people, receipt);
        if (receipt.SplitMode == SplitMode.Items)
            ctx.SplitByItem();
        else
            ctx.SplitByWeight();
        ctx.WarnAboutLargeDiscounts();

        var computed = receipt.Lines.Sum(l => l.LineTotal) + ctx.Counted.Sum(a => a.Amount);
        var breakdown = ctx.ToBreakdown(computed);

        // Step 10. Allocation always sums exactly, so a gap here is a bug, never bad input.
        var expected = receipt.SplitMode == SplitMode.Items ? computed : receipt.ConfirmedTotal;
        var actual = breakdown.People.Sum(p => p.Total) + breakdown.Unclaimed.Total;
        if (actual != expected)
            throw new InvalidOperationException($"Receipt {receipt.Id} allocated {actual}, expected {expected}.");

        return breakdown;
    }

    private sealed class Context
    {
        private readonly Receipt _receipt;
        private readonly IReadOnlyList<Person> _people;
        private readonly Dictionary<string, int> _index;
        private readonly int[] _participants;
        private readonly int _unclaimed;
        private readonly long[] _subtotals;
        private readonly Dictionary<string, long[]> _adjustmentShares = new();
        private readonly Dictionary<string, long[]> _unitShares = new();
        private readonly Dictionary<string, int[]> _unitClaimants = new();
        private readonly List<UnitShare> _items = [];
        private readonly List<ReceiptWarning> _warnings = [];

        public Context(IReadOnlyList<Person> people, Receipt receipt)
        {
            if (people.Count == 0)
                throw new ArgumentException("A bill needs at least one person.", nameof(people));

            _receipt = receipt;
            _people = people;
            _index = new Dictionary<string, int>(people.Count);
            for (var i = 0; i < people.Count; i++)
            {
                if (!_index.TryAdd(people[i].Id, i))
                    throw new ArgumentException($"Person {people[i].Id} appears twice.", nameof(people));
            }

            _unclaimed = people.Count;
            _subtotals = new long[people.Count + 1];
            _participants = receipt.Participants.Count == 0
                ? Enumerable.Range(0, people.Count).ToArray()
                : IndexesOf(receipt.Participants);
            Counted = receipt.Adjustments.Where(a => !a.IncludedInPrices).ToList();
        }

        /// <summary>Adjustments that add to the total. Tax already in prices is shown only.</summary>
        public List<Adjustment> Counted { get; }

        /// <summary>Steps 3 to 7: units, shared units, food subtotals, then each adjustment.</summary>
        public void SplitByItem()
        {
            foreach (var line in _receipt.Lines)
            {
                if (line.Units.Count == 0)
                {
                    _subtotals[_unclaimed] += line.LineTotal;
                    continue;
                }

                var unitPrices = Allocation.Evenly(line.LineTotal, line.Units.Count);
                for (var u = 0; u < line.Units.Count; u++)
                {
                    var unit = line.Units[u];
                    var (claimants, shares) = SplitUnit(unit, unitPrices[u]);
                    if (!_unitShares.TryAdd(unit.Id, shares))
                        throw new ArgumentException($"Unit {unit.Id} appears twice.", nameof(_receipt));
                    _unitClaimants[unit.Id] = claimants;
                    for (var b = 0; b < shares.Length; b++)
                        _subtotals[b] += shares[b];
                    if (claimants.Length == 0)
                        _items.Add(new UnitShare(unit.Id, null, shares[_unclaimed]));
                    foreach (var p in claimants)
                        _items.Add(new UnitShare(unit.Id, _people[p].Id, shares[p]));
                }
            }

            foreach (var adjustment in Counted)
                _adjustmentShares[adjustment.Id] = Allocate(adjustment, ItemWeights(adjustment), ScopePeople(adjustment) ?? _participants);
        }

        /// <summary>
        /// Step 1, equal and shares modes. Claims are ignored. Person-scoped adjustments come out first,
        /// the rest is split by weight, then each person-scoped amount goes back to its own people.
        /// </summary>
        public void SplitByWeight()
        {
            var weights = new long[_unclaimed + 1];
            foreach (var p in _participants)
                weights[p] = _receipt.SplitMode == SplitMode.Shares ? ShareOf(_people[p].Id) : 1;
            if (weights.All(w => w == 0))
                throw new ArgumentException($"Receipt {_receipt.Id} has no one to split between.", nameof(_receipt));

            var personScoped = Counted.Where(a => a.IsPersonScoped).ToList();
            var baseTotal = _receipt.ConfirmedTotal - personScoped.Sum(a => a.Amount);
            var baseShares = Allocation.Allocate(baseTotal, weights);
            baseShares.CopyTo(_subtotals, 0);

            foreach (var adjustment in personScoped)
            {
                var scope = ScopePeople(adjustment)!;
                var byBase = new long[_unclaimed + 1];
                foreach (var p in scope)
                    byBase[p] = Math.Max(0, baseShares[p]);
                _adjustmentShares[adjustment.Id] = Allocate(adjustment, byBase, scope);
            }
        }

        public void WarnAboutLargeDiscounts()
        {
            for (var p = 0; p < _unclaimed; p++)
            {
                long ownDiscounts = 0;
                string? first = null;
                foreach (var adjustment in Counted.Where(a => a.IsPersonScoped && a.Amount < 0))
                {
                    var share = _adjustmentShares[adjustment.Id][p];
                    if (share == 0) continue;
                    ownDiscounts += share;
                    first ??= adjustment.Id;
                }
                if (-ownDiscounts > _subtotals[p])
                    _warnings.Add(new ReceiptWarning(WarningCode.DiscountExceedsClaims, first, _people[p].Id));
            }
        }

        public ReceiptBreakdown ToBreakdown(long computed)
        {
            PersonShare Bucket(int b) => new(
                b == _unclaimed ? null : _people[b].Id,
                _subtotals[b],
                _adjustmentShares.ToDictionary(kv => kv.Key, kv => kv.Value[b]));

            return new ReceiptBreakdown(
                _receipt.Id,
                Enumerable.Range(0, _unclaimed).Select(Bucket).ToList(),
                Bucket(_unclaimed),
                computed,
                _receipt.ConfirmedTotal,
                _warnings,
                _items);
        }

        /// <summary>Step 4: a unit's price over its claimants by weight, or to Unclaimed.</summary>
        private (int[] Claimants, long[] Shares) SplitUnit(Unit unit, long price)
        {
            var shares = new long[_unclaimed + 1];
            var byPerson = new SortedDictionary<int, long>();
            foreach (var claim in unit.Claims)
            {
                if (claim.Weight <= 0)
                    throw new ArgumentException($"Claim weights must be positive (unit {unit.Id}).", nameof(unit));
                var p = IndexOf(claim.PersonId);
                byPerson[p] = byPerson.GetValueOrDefault(p) + claim.Weight;
            }

            if (byPerson.Count == 0)
            {
                shares[_unclaimed] = price;
                return ([], shares);
            }

            var claimants = byPerson.Keys.ToArray();
            var split = Allocation.Allocate(price, byPerson.Values.ToArray());
            for (var i = 0; i < claimants.Length; i++)
                shares[claimants[i]] = split[i];
            return (claimants, shares);
        }

        /// <summary>Steps 6 and 7: what each bucket is weighted by for one adjustment in items mode.</summary>
        private long[] ItemWeights(Adjustment adjustment)
        {
            var weights = new long[_unclaimed + 1];
            var scopePeople = ScopePeople(adjustment);
            var scopeUnits = adjustment.Scope?.Units is { Count: > 0 } units ? units : null;

            if (adjustment.Split == AdjustmentSplit.Equal)
            {
                var who = scopePeople
                    ?? scopeUnits?.SelectMany(UnitClaimants).Distinct().ToArray()
                    ?? _participants;
                foreach (var p in who)
                    weights[p] = 1;
                return weights;
            }

            if (scopeUnits is null)
                _subtotals.CopyTo(weights, 0);
            else
                foreach (var unitId in scopeUnits)
                {
                    var shares = UnitShares(unitId);
                    for (var b = 0; b < weights.Length; b++)
                        weights[b] += shares[b];
                }

            if (scopePeople is not null)
            {
                var keep = new HashSet<int>(scopePeople);
                for (var b = 0; b < weights.Length; b++)
                    if (!keep.Contains(b)) weights[b] = 0;
            }

            // A negative unit price (a free item printed as a minus line) cannot be a weight.
            for (var b = 0; b < weights.Length; b++)
                weights[b] = Math.Max(0, weights[b]);
            return weights;
        }

        /// <summary>Allocates one adjustment, falling back to an equal split when nothing in scope has weight.</summary>
        private long[] Allocate(Adjustment adjustment, long[] weights, int[] fallback)
        {
            if (weights.Any(w => w > 0))
                return Allocation.Allocate(adjustment.Amount, weights);

            _warnings.Add(new ReceiptWarning(WarningCode.NothingInScope, adjustment.Id));
            var equal = new long[_unclaimed + 1];
            foreach (var p in fallback)
                equal[p] = 1;
            if (fallback.Length == 0)
                equal[_unclaimed] = 1;
            return Allocation.Allocate(adjustment.Amount, equal);
        }

        private int[]? ScopePeople(Adjustment adjustment) =>
            adjustment.Scope?.People is { Count: > 0 } ids ? IndexesOf(ids) : null;

        private long ShareOf(string personId)
        {
            var shares = _receipt.Shares;
            if (shares is null) return 1;
            foreach (var id in shares.Keys)
                IndexOf(id);
            return shares.GetValueOrDefault(personId, 1);
        }

        private long[] UnitShares(string unitId) =>
            _unitShares.TryGetValue(unitId, out var shares)
                ? shares
                : throw new ArgumentException($"Unknown unit {unitId}.", nameof(unitId));

        private int[] UnitClaimants(string unitId)
        {
            UnitShares(unitId);
            return _unitClaimants[unitId];
        }

        private int[] IndexesOf(IEnumerable<string> personIds) =>
            personIds.Select(IndexOf).Distinct().Order().ToArray();

        private int IndexOf(string personId) =>
            _index.TryGetValue(personId, out var i)
                ? i
                : throw new ArgumentException($"Unknown person {personId}.", nameof(personId));
    }
}
