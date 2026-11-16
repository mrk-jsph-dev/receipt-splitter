using System.Net.Http.Json;
using System.Net;
using ReceiptSplitter.Api.Features.Receipts;

namespace ReceiptSplitter.Api.Tests.Features.Receipts;

public class CheckReceiptTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object[] RamenLines(long kakuni) =>
    [
        new { name = "Butao King", quantity = 8, unitPrice = 48500 },
        new { name = "Classic Gyoza", quantity = 1, unitPrice = 19000 },
        new { name = "Coke Zero", quantity = 1, unitPrice = 11500 },
        new { name = "Kaedama (thick)", quantity = 1, unitPrice = 9500 },
        new { name = "Kaedama (thin)", quantity = 3, unitPrice = 9500 },
        new { name = "Gyu King", quantity = 2, unitPrice = 79500 },
        new { name = "Kakuni Belly", quantity = 1, unitPrice = kakuni },
        new { name = "Red King", quantity = 2, unitPrice = 59500 },
        new { name = "Tamago", quantity = 1, unitPrice = 8500 },
    ];

    private static object RamenDraft(long kakuni) => new
    {
        label = "Dinner",
        confirmedTotal = 525580,
        lines = RamenLines(kakuni),
        adjustments = new object[]
        {
            new { kind = "service", label = "SERVICE CHARGE", amount = 68080, split = "equal" },
            new { kind = "discount", label = "40% BDO OFF", amount = -305000, rate = 4000 },
        },
    };

    private static async Task<(Created Created, string Url)> ManualBillAsync(HttpClient client, params string[] people)
    {
        var response = await client.PostAsJsonAsync("/api/bills", new { source = "manual", organizerName = people[0], currency = new { code = "PHP", digits = 2 } });
        var created = (await response.ReadAsync<Created>())!;
        foreach (var name in people.Skip(1))
            await client.PatchAsync(created.Id, new { op = "addPerson", name });
        return (created, $"/api/bills/{created.Id}/receipts/check");
    }

    [Fact]
    public async Task A_misread_price_shows_the_gap_and_fixing_it_matches()
    {
        using var client = factory.NewClient();
        var (created, url) = await ManualBillAsync(client, "Ana");

        var misread = await (await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { draft = RamenDraft(10500) })).ReadAsync<ReceiptCheck>();
        var fixedUp = await (await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { draft = RamenDraft(19500) })).ReadAsync<ReceiptCheck>();

        // Walkthrough step 6: adds up to 5,165.80, 90.00 short of 5,255.80.
        Assert.Equal(753500, misread!.Items);
        Assert.Equal([388000, 19000, 11500, 9500, 28500, 159000, 10500, 119000, 8500], misread.LineTotals);
        Assert.Equal(516580, misread.ComputedTotal);
        Assert.Equal(-9000, misread.Difference);
        Assert.False(misread.Matches);
        Assert.True(fixedUp!.Matches);
        Assert.Equal(-236920, fixedUp.Adjustments);
        Assert.Equal([null, -305000], fixedUp.Suggested);
    }

    [Fact]
    public async Task The_senior_lunch_gives_400_and_three_times_560()
    {
        using var client = factory.NewClient();
        var (created, url) = await ManualBillAsync(client, "Ana", "Ben", "Carla", "Lola");
        var lola = (await client.GetBillAsync(created.Id)).Bill.PersonId("Lola");
        var draft = new
        {
            label = "Lunch",
            confirmedTotal = 208000,
            splitMode = "equal",
            lines = new[] { new { name = "Food", quantity = 1, unitPrice = 224000 } },
            adjustments = new object[]
            {
                new { kind = "vatExempt", label = "Less VAT, senior share", amount = -6000, scope = new { people = new[] { lola } } },
                new { kind = "senior", label = "SC DISC 20%", amount = -10000, scope = new { people = new[] { lola } } },
            },
        };

        var check = await (await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { draft })).ReadAsync<ReceiptCheck>();

        Assert.True(check!.Matches);
        Assert.Equal([56000, 56000, 56000, 40000], check.Breakdown!.People.Select(p => p.Total));
    }

    [Fact]
    public async Task Checking_needs_the_organizer_key_and_saves_nothing()
    {
        using var client = factory.NewClient();
        var (created, url) = await ManualBillAsync(client, "Ana");

        var noKey = await client.OrganizerAsync(HttpMethod.Post, url, null, new { draft = RamenDraft(19500) });
        var ok = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { draft = RamenDraft(19500) });

        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Empty((await client.GetBillAsync(created.Id)).Bill.Receipts);
    }
}
