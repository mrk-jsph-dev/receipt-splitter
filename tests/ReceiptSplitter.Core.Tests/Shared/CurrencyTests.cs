using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Shared;

public class CurrencyTests
{
    private static readonly Currency Jpy = new("JPY", 0);
    private static readonly Currency Kwd = new("KWD", 3);
    private static readonly Currency Php = new("PHP", 2);

    [Fact]
    public void Converts_between_decimal_and_minor_units()
    {
        Assert.Equal(1000, Jpy.ToMinor(1000m));
        Assert.Equal(12345, Kwd.ToMinor(12.345m));
        Assert.Equal(525580, Php.ToMinor(5255.80m));
        Assert.Equal(5255.80m, Php.ToDecimal(525580));
        Assert.Equal(-3.050m, Kwd.ToDecimal(-3050));
    }

    [Fact]
    public void Refuses_amounts_finer_than_the_minor_unit()
    {
        Assert.Throws<ArgumentException>(() => Php.ToMinor(1.234m));
        Assert.Throws<ArgumentException>(() => Jpy.ToMinor(0.5m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Currency("XXX", 4));
    }

    [Fact]
    public void Yen_bill_splits_in_whole_yen()
    {
        var people = People("a", "b", "c");
        var receipt = Receipt(Jpy.ToMinor(1000m), [Line("ramen", 1, Jpy.ToMinor(1000m), By("a", "b", "c"))]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([334L, 333, 333], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Dinar_bill_splits_in_fils()
    {
        var people = People("a", "b", "c");
        var receipt = Receipt(
            confirmedTotal: Kwd.ToMinor(11.001m),
            lines: [Line("mezze", 1, Kwd.ToMinor(10.000m), By("a", "b", "c"))],
            adjustments: [new Adjustment("svc", AdjustmentKind.Service, "Service", Kwd.ToMinor(1.001m))]);

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.True(result.Matches);
        Assert.Equal([3334L + 334, 3333 + 334, 3333 + 333], result.People.Select(p => p.Total));
    }
}
