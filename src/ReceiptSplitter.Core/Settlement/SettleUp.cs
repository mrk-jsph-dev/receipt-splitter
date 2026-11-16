using ReceiptSplitter.Core.Bills;

namespace ReceiptSplitter.Core.Settlement;

public sealed record Transfer(string FromPersonId, string ToPersonId, long Amount);

/// <summary>Who pays whom, netted once across the whole bill. See docs/SPEC.md step 11.</summary>
public static class SettleUp
{
    /// <summary>
    /// Repeatedly matches the largest debtor with the largest creditor and moves the smaller of the
    /// two amounts, ties going to the earlier person. Each transfer clears at least one person, so
    /// there are at most n − 1. Same bill in, same list out.
    /// </summary>
    public static IReadOnlyList<Transfer> Transfers(BillBreakdown bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        if (!bill.CanSettle)
            throw new InvalidOperationException("Every receipt must match its total, be fully claimed and be fully paid before settling up.");

        var net = bill.People.Select(p => p.Net).ToArray();
        if (net.Sum() != 0)
            throw new InvalidOperationException("Balances do not net to zero.");

        var transfers = new List<Transfer>();
        while (true)
        {
            int debtor = -1, creditor = -1;
            for (var i = 0; i < net.Length; i++)
            {
                if (net[i] < 0 && (debtor < 0 || net[i] < net[debtor])) debtor = i;
                if (net[i] > 0 && (creditor < 0 || net[i] > net[creditor])) creditor = i;
            }
            if (debtor < 0) break;

            var amount = Math.Min(-net[debtor], net[creditor]);
            transfers.Add(new Transfer(bill.People[debtor].PersonId, bill.People[creditor].PersonId, amount));
            net[debtor] += amount;
            net[creditor] -= amount;
        }

        return transfers;
    }
}
