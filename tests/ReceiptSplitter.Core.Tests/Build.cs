namespace ReceiptSplitter.Core.Tests;

/// <summary>Short helpers for writing receipts as test data.</summary>
internal static class Build
{
    public static IReadOnlyList<Person> People(params string[] ids) =>
        ids.Select(id => new Person(id, char.ToUpperInvariant(id[0]) + id[1..])).ToList();

    /// <summary>Claimants for one unit, each with weight 1.</summary>
    public static Claim[] By(params string[] personIds) =>
        personIds.Select(id => new Claim(id)).ToArray();

    /// <summary>
    /// A line of <paramref name="quantity"/> units. <paramref name="unitClaims"/> gives the claims
    /// for the first units in order; any units after that are unclaimed.
    /// </summary>
    public static Line Line(string key, int quantity, long unitPrice, params Claim[][] unitClaims) =>
        Line(key, quantity, checked(quantity * unitPrice), unitPrice, unitClaims);

    public static Line Line(string key, int quantity, long lineTotal, long unitPrice, params Claim[][] unitClaims)
    {
        var units = Enumerable.Range(0, quantity)
            .Select(i => new Unit($"{key}-{i + 1}", i < unitClaims.Length ? unitClaims[i] : []))
            .ToList();
        return new Line(key, key, quantity, unitPrice, lineTotal, units);
    }

    public static Receipt Receipt(
        long confirmedTotal,
        IReadOnlyList<Line> lines,
        IReadOnlyList<Adjustment>? adjustments = null,
        IReadOnlyList<string>? participants = null,
        IReadOnlyList<Payer>? payers = null,
        SplitMode splitMode = SplitMode.Items,
        IReadOnlyDictionary<string, long>? shares = null,
        string id = "r1") =>
        new(id, id, confirmedTotal, lines, adjustments ?? [], participants ?? [], payers ?? [], splitMode, shares);

    public static long TotalOf(this ReceiptBreakdown breakdown, string personId) =>
        breakdown.People.Single(p => p.PersonId == personId).Total;
}
