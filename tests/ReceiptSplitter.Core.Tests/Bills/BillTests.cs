using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Bills;

public class BillTests
{
    [Fact]
    public void Two_receipts_net_once_for_the_whole_bill()
    {
        var bill = BillCalculator.Calculate(SampleBill.People, [SampleBill.Dinner(), SampleBill.MilkTea()]);

        var ben = bill.People.Single(p => p.PersonId == "ben");
        Assert.Equal(38574 + 15000, ben.Owed);
        Assert.Equal(60000, ben.Paid);
        Assert.Equal(6426, ben.Net);
        Assert.Equal(470106, bill.People.Single(p => p.PersonId == "ana").Net);
        Assert.Equal(-68374, bill.People.Single(p => p.PersonId == "carla").Net);
        Assert.Equal(0, bill.People.Sum(p => p.Net));
        Assert.True(bill.CanSettle);
    }

    [Fact]
    public void Settle_up_for_two_receipts_has_eleven_transfers_and_ben_sends_nothing()
    {
        var bill = BillCalculator.Calculate(SampleBill.People, [SampleBill.Dinner(), SampleBill.MilkTea()]);

        var transfers = SettleUp.Transfers(bill);

        Assert.Equal(11, transfers.Count);
        Assert.DoesNotContain(transfers, t => t.FromPersonId == "ben");
        Assert.Equal(new Transfer("eli", "ben", 6426), Assert.Single(transfers, t => t.ToPersonId == "ben"));
        Assert.Equal(new Transfer("carla", "ana", 68374), transfers[0]);
    }

    [Fact]
    public void A_receipts_fees_never_touch_another_receipt()
    {
        var milkTea = SampleBill.MilkTea();

        var alone = ReceiptCalculator.Calculate(SampleBill.People, milkTea);
        var bill = BillCalculator.Calculate(SampleBill.People, [SampleBill.Dinner(), milkTea]);

        Assert.Equal(alone.People.Select(p => p.Total), bill.Receipts[1].People.Select(p => p.Total));
        Assert.All(bill.Receipts[1].People, p => Assert.Empty(p.Adjustments));
    }

    [Fact]
    public void Equal_fee_uses_only_that_receipts_participants()
    {
        var milkTea = SampleBill.MilkTea() with
        {
            ConfirmedTotal = 60400,
            Adjustments = [new Adjustment("delivery", AdjustmentKind.Fee, "Delivery", 400, AdjustmentSplit.Equal)],
        };

        var result = ReceiptCalculator.Calculate(SampleBill.People, milkTea);

        Assert.Equal([15100L, 15100, 15100, 15100, 0, 0, 0, 0, 0, 0, 0, 0], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Settle_up_with_two_payers_takes_at_most_n_minus_one_transfers_and_clears_everyone()
    {
        var people = People("a", "b", "c", "d", "e");
        var receipt = Receipt(10000, [Line("x", 1, 10000)], splitMode: SplitMode.Equal,
            payers: [new Payer("b", 7000), new Payer("d", 3000)]);
        var bill = BillCalculator.Calculate(people, [receipt]);

        var transfers = SettleUp.Transfers(bill);

        Assert.InRange(transfers.Count, 1, people.Count - 1);
        var net = bill.People.ToDictionary(p => p.PersonId, p => p.Net);
        foreach (var t in transfers)
        {
            Assert.True(t.Amount > 0);
            net[t.FromPersonId] += t.Amount;
            net[t.ToPersonId] -= t.Amount;
        }
        Assert.All(net.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Settle_up_is_blocked_until_every_receipt_is_ready()
    {
        var people = People("a", "b");
        var unclaimed = Receipt(1000, [Line("x", 1, 1000)], payers: [new Payer("a", 1000)], id: "unclaimed");
        var mismatch = Receipt(999, [Line("x", 1, 1000, By("a"))], payers: [new Payer("a", 999)], id: "mismatch");
        var unpaid = Receipt(1000, [Line("x", 1, 1000, By("a"))], payers: [new Payer("a", 900)], id: "unpaid");

        foreach (var receipt in new[] { unclaimed, mismatch, unpaid })
        {
            var bill = BillCalculator.Calculate(people, [receipt]);

            Assert.False(bill.CanSettle);
            Assert.Contains(bill.Problems, p => p.ReceiptId == receipt.Id);
            Assert.Throws<InvalidOperationException>(() => SettleUp.Transfers(bill));
        }
    }
}
