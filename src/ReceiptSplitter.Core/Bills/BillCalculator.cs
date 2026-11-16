using ReceiptSplitter.Core.Receipts;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Core.Bills;

/// <summary>What one person owes and paid across every receipt. Positive net means they get money back.</summary>
public sealed record PersonBalance(string PersonId, long Owed, long Paid)
{
    public long Net => checked(Paid - Owed);
}

public enum ProblemCode
{
    /// <summary>Lines and fees do not add up to the printed total.</summary>
    TotalMismatch,

    /// <summary>Some items have not been claimed yet.</summary>
    UnclaimedItems,

    /// <summary>What the payers paid does not add up to the printed total.</summary>
    PayersDoNotMatchTotal,
}

/// <param name="Amount">The gap in minor units: computed − printed, unclaimed amount, or paid − printed.</param>
public sealed record BillProblem(ProblemCode Code, string ReceiptId, long Amount);

public sealed record BillBreakdown(
    IReadOnlyList<ReceiptBreakdown> Receipts,
    IReadOnlyList<PersonBalance> People,
    IReadOnlyList<BillProblem> Problems)
{
    public bool CanSettle => Problems.Count == 0;
}

/// <summary>Step 11: totals across receipts. Each receipt is worked out on its own first.</summary>
public static class BillCalculator
{
    public static BillBreakdown Calculate(IReadOnlyList<Person> people, IReadOnlyList<Receipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(receipts);

        var owed = new long[people.Count];
        var paid = new long[people.Count];
        var index = people.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var breakdowns = new List<ReceiptBreakdown>(receipts.Count);
        var problems = new List<BillProblem>();

        foreach (var receipt in receipts)
        {
            var breakdown = ReceiptCalculator.Calculate(people, receipt);
            breakdowns.Add(breakdown);

            if (!breakdown.Matches)
                problems.Add(new BillProblem(ProblemCode.TotalMismatch, receipt.Id, breakdown.Difference));
            if (breakdown.Unclaimed.Subtotal != 0 || breakdown.Unclaimed.Total != 0)
                problems.Add(new BillProblem(ProblemCode.UnclaimedItems, receipt.Id, breakdown.Unclaimed.Total));

            var paidOnReceipt = receipt.Payers.Sum(p => p.Amount);
            if (paidOnReceipt != receipt.ConfirmedTotal)
                problems.Add(new BillProblem(ProblemCode.PayersDoNotMatchTotal, receipt.Id, paidOnReceipt - receipt.ConfirmedTotal));

            for (var p = 0; p < people.Count; p++)
                owed[p] += breakdown.People[p].Total;
            foreach (var payer in receipt.Payers)
            {
                if (!index.TryGetValue(payer.PersonId, out var p))
                    throw new ArgumentException($"Unknown payer {payer.PersonId} on receipt {receipt.Id}.", nameof(receipts));
                paid[p] += payer.Amount;
            }
        }

        var balances = people.Select((person, p) => new PersonBalance(person.Id, owed[p], paid[p])).ToList();
        return new BillBreakdown(breakdowns, balances, problems);
    }
}
