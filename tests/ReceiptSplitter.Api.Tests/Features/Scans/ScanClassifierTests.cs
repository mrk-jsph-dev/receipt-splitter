using ReceiptSplitter.Api.Features.Scans;
using ReceiptSplitter.Api.Infrastructure.Scanning;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Scans;

public class ScanClassifierTests
{
    private static readonly CurrencyInfo Php = new("PHP", 2);

    private static ScannedReceipt Receipt(params ScannedItem[] items) => new("Shop", null, "PHP", null, null, null, null, items);

    [Fact]
    public async Task The_Ramen_Nagi_photo_gives_9_lines_and_flags_the_service_charge_and_discount()
    {
        var scan = await new SampleReceiptReader().ReadAsync(BinaryData.FromString("photo"), default);

        var draft = ScanClassifier.ToDraft(scan, Php);

        Assert.Equal(9, draft.Lines.Count);
        Assert.Equal(525580, draft.ConfirmedTotal);
        Assert.Equal("Ramen Nagi", draft.Merchant);
        var kakuni = draft.Lines.Single(l => l.Name == "Kakuni Belly");
        Assert.Equal(10500, kakuni.UnitPrice);
        Assert.True(kakuni.Confidence < (decimal)ScanClassifier.Unsure);
        Assert.Equal(48500, draft.Lines[0].UnitPrice);
        Assert.Equal(8, draft.Lines[0].Quantity);

        var service = draft.Adjustments!.Single(a => a.Kind == AdjustmentKind.Service);
        Assert.Equal(68080, service.Amount);
        Assert.Equal(AdjustmentSplit.Equal, service.Split);
        var discount = draft.Adjustments!.Single(a => a.Kind == AdjustmentKind.Discount);
        Assert.Equal(-305000, discount.Amount);
        Assert.Equal(4000, discount.Rate);
    }

    [Theory]
    [InlineData("SC DISC 20%", AdjustmentKind.Senior)]
    [InlineData("Senior Citizen Discount", AdjustmentKind.Senior)]
    [InlineData("PWD DISC", AdjustmentKind.Pwd)]
    [InlineData("VAT EXEMPT SALES", AdjustmentKind.VatExempt)]
    [InlineData("Less VAT", AdjustmentKind.VatExempt)]
    [InlineData("Svc Chg", AdjustmentKind.Service)]
    [InlineData("S/C 10%", AdjustmentKind.Service)]
    [InlineData("Promo 10% off", AdjustmentKind.Discount)]
    [InlineData("VAT 12%", AdjustmentKind.Tax)]
    [InlineData("Tip", AdjustmentKind.Tip)]
    public void Words_on_the_line_pick_the_kind(string text, AdjustmentKind kind) =>
        Assert.Equal(kind, ScanClassifier.Classify(text, 1000));

    [Theory]
    [InlineData("Tonkotsu Ramen")]
    [InlineData("Coffee")]
    [InlineData("Taxi Burger")]
    public void Ordinary_items_stay_items(string text) => Assert.Null(ScanClassifier.Classify(text, 1000));

    [Fact]
    public void A_negative_line_is_a_discount_and_discounts_are_always_negative()
    {
        var draft = ScanClassifier.ToDraft(Receipt(new ScannedItem("Rice", 1, 50m, 50m, 0.9), new ScannedItem("Manager comp", null, null, -20m, 0.9), new ScannedItem("SC DISC", null, null, 100m, 0.9)), Php);

        Assert.Equal(-2000, draft.Adjustments!.Single(a => a.Kind == AdjustmentKind.Discount).Amount);
        Assert.Equal(-10000, draft.Adjustments!.Single(a => a.Kind == AdjustmentKind.Senior).Amount);
        Assert.Single(draft.Lines);
    }

    [Fact]
    public void A_deal_keeps_its_printed_line_total()
    {
        var draft = ScanClassifier.ToDraft(Receipt(new ScannedItem("Gyoza 3 for 100", 3, 33.34m, 100m, 0.9)), Php);

        var line = Assert.Single(draft.Lines);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(10000, line.LineTotal);
    }

    [Fact]
    public void Tax_counts_on_top_only_when_the_receipt_numbers_say_so()
    {
        var onTop = ScanClassifier.ToDraft(new ScannedReceipt(null, null, null, 112m, 100m, 12m, null, [new("Pasta", 1, 100m, 100m, 0.9)]), Php);
        var inPrices = ScanClassifier.ToDraft(new ScannedReceipt(null, null, null, 100m, 100m, 10.71m, null, [new("Pasta", 1, 100m, 100m, 0.9)]), Php);

        Assert.False(onTop.Adjustments!.Single().IncludedInPrices);
        Assert.True(inPrices.Adjustments!.Single().IncludedInPrices);
    }

    [Fact]
    public void Amounts_round_to_the_currency_digits()
    {
        Assert.Equal(19500, ScanClassifier.Minor(195.00m, Php));
        Assert.Equal(1200, ScanClassifier.Minor(1200m, new CurrencyInfo("JPY", 0)));
        Assert.Equal(1235, ScanClassifier.Minor(1.2345m, new CurrencyInfo("KWD", 3)));
    }

    [Fact]
    public void A_second_photo_appends_its_lines_and_brings_the_total()
    {
        var first = ScanClassifier.ToDraft(new ScannedReceipt("Ramen Nagi", null, null, null, null, null, null, [new("Butao King", 8, 485m, 3880m, 0.9)]), Php);
        var second = ScanClassifier.ToDraft(new ScannedReceipt(null, null, null, 5255.80m, null, null, null, [new("Tamago", 1, 85m, 85m, 0.9)]), Php);

        var both = ScanClassifier.Append(first, second);

        Assert.Equal(["Butao King", "Tamago"], both.Lines.Select(l => l.Name));
        Assert.Equal(525580, both.ConfirmedTotal);
        Assert.Equal("Ramen Nagi", both.Merchant);
    }
}
