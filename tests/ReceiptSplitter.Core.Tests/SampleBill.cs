using static ReceiptSplitter.Core.Tests.Build;

namespace ReceiptSplitter.Core.Tests;

/// <summary>
/// The Ramen Nagi receipt with made-up names, as in
/// docs/SPEC.md. Who claimed what for Fay to Lia is not in the spec; it was chosen so that every
/// published number holds.
/// </summary>
internal static class SampleBill
{
    public static readonly IReadOnlyList<Person> People =
        Build.People("ana", "ben", "carla", "dan", "eli", "fay", "gio", "hana", "ivan", "jo", "kai", "lia");

    public static Receipt Dinner(SplitMode splitMode = SplitMode.Items) => Receipt(
        confirmedTotal: 525580,
        lines:
        [
            Line("butao", 8, 48500,
                By("ana"), By("ben"), By("eli"), By("fay"), By("gio"), By("hana"), By("kai"), By("lia")),
            Line("gyoza", 1, 19000, By("ben", "fay", "gio")),
            Line("coke-zero", 1, 11500, By("dan")),
            Line("noodles-thick", 1, 9500, By("jo")),
            Line("noodles-thin", 3, 9500, By("ana"), By("gio"), By("lia")),
            Line("gyu", 2, 79500, By("carla"), By("ivan")),
            Line("kakuni", 1, 19500, By("hana")),
            Line("red", 2, 59500, By("dan"), By("jo")),
            Line("tamago", 1, 8500, By("kai")),
        ],
        adjustments:
        [
            new Adjustment("service", AdjustmentKind.Service, "SERVICE CHARGE", 68080, AdjustmentSplit.Equal),
            new Adjustment("bdo", AdjustmentKind.Discount, "40% BDO OFF", -305000, AdjustmentSplit.Proportional),
        ],
        payers: [new Payer("ana", 525580)],
        splitMode: splitMode,
        id: "dinner");

    public static Receipt MilkTea() => Receipt(
        confirmedTotal: 60000,
        lines: [Line("milk-tea", 4, 15000, By("ana"), By("ben"), By("carla"), By("dan"))],
        participants: ["ana", "ben", "carla", "dan"],
        payers: [new Payer("ben", 60000)],
        id: "milk-tea");
}
