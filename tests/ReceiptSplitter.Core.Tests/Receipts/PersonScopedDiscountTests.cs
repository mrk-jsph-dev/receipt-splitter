using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests.Receipts;

/// <summary>A senior lunch: four people, ₱2,240.00 of food, Lola has a senior card.</summary>
public class PersonScopedDiscountTests
{
    private static readonly IReadOnlyList<Person> People =
        [new("lola", "Lola", Card.Senior), new("ana", "Ana"), new("ben", "Ben"), new("carla", "Carla")];

    private static readonly IReadOnlyList<Adjustment> SeniorLines =
    [
        new("less-vat", AdjustmentKind.VatExempt, "LESS VAT SC", -6000, Scope: new AdjustmentScope(People: ["lola"])),
        new("sc-disc", AdjustmentKind.Senior, "SC DISC 20%", -10000, Scope: new AdjustmentScope(People: ["lola"])),
    ];

    private static Receipt Lunch(SplitMode mode) => Receipt(
        confirmedTotal: 208000,
        lines:
        [
            Line("set-a", 1, 56000, By("lola")), Line("set-b", 1, 56000, By("ana")),
            Line("set-c", 1, 56000, By("ben")), Line("set-d", 1, 56000, By("carla")),
        ],
        adjustments: SeniorLines,
        splitMode: mode,
        shares: mode == SplitMode.Shares ? People.ToDictionary(p => p.Id, _ => 1L) : null);

    [Theory]
    [InlineData(SplitMode.Items)]
    [InlineData(SplitMode.Equal)]
    [InlineData(SplitMode.Shares)]
    public void Lola_pays_400_and_the_others_560_in_every_mode(SplitMode mode)
    {
        var result = ReceiptCalculator.Calculate(People, Lunch(mode));

        Assert.True(result.Matches);
        Assert.Equal([40000L, 56000, 56000, 56000], result.People.Select(p => p.Total));
        Assert.Equal(-16000, result.People[0].Adjustments.Values.Sum());
    }

    [Fact]
    public void Two_card_holders_on_one_line_split_it_by_their_share_of_the_base()
    {
        var people = Build.People("lola", "lolo", "ana", "ben");
        var receipt = Receipt(
            confirmedTotal: 80000 - 9000,
            lines: [Line("food", 1, 80000)],
            adjustments: [new Adjustment("sc", AdjustmentKind.Senior, "SC DISC", -9000,
                Scope: new AdjustmentScope(People: ["lola", "lolo"]))],
            splitMode: SplitMode.Shares,
            shares: new Dictionary<string, long> { ["lola"] = 2, ["lolo"] = 1, ["ana"] = 1, ["ben"] = 1 });

        var result = ReceiptCalculator.Calculate(people, receipt);

        Assert.Equal([32000L - 6000, 16000 - 3000, 16000, 16000], result.People.Select(p => p.Total));
    }

    [Fact]
    public void Discount_larger_than_the_holders_claims_warns()
    {
        var receipt = Lunch(SplitMode.Items) with
        {
            Lines = [Line("set-a", 1, 10000, By("lola")), Line("rest", 1, 214000, By("ana"))],
        };

        var result = ReceiptCalculator.Calculate(People, receipt);

        Assert.Contains(result.Warnings, w => w.Code == WarningCode.DiscountExceedsClaims);
    }
}
