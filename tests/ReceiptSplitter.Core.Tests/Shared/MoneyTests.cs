
namespace ReceiptSplitter.Core.Tests.Shared;

public class MoneyTests
{
    [Fact]
    public void Money_is_stored_in_minor_units()
    {
        var total = new Money(525580, "PHP");

        Assert.Equal(525580L, total.Minor);
    }
}
