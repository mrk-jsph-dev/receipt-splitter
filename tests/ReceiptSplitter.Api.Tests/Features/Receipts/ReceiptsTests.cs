using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Receipts;

public class ReceiptsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object LineOf(LineDoc line, int? quantity = null) =>
        new { id = line.Id, name = line.Name, quantity = quantity ?? line.Quantity, unitPrice = line.UnitPrice };

    /// <summary>The sample receipt as a replace body, with lines changed by <paramref name="lines"/>.</summary>
    private static object DinnerDraft(ReceiptDoc receipt, IEnumerable<object> lines, long? confirmedTotal = null) => new
    {
        label = receipt.Label,
        merchant = receipt.Merchant,
        confirmedTotal = confirmedTotal ?? receipt.ConfirmedTotal,
        lines,
        adjustments = receipt.Adjustments.Select(a => new { id = a.Id, a.Kind, a.Label, a.Amount, a.Rate, a.Split }),
        payers = receipt.Payers,
    };

    [Fact]
    public async Task Adding_the_sample_as_a_second_receipt_keeps_each_receipt_separate()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/receipts", created.OrganizerKey, new { source = "sample" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (bill, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Equal(2, bill.Receipts.Count);
        Assert.NotEqual(bill.Receipts[0].Id, bill.Receipts[1].Id);
        Assert.Empty(bill.Receipts[1].Payers);
        Assert.Equal("Dinner was added", bill.Activity[^1].Text);
        Assert.All(breakdown!.Receipts, r => Assert.True(r.Matches));
    }

    [Fact]
    public async Task A_bill_holds_at_most_10_receipts_and_manual_receipts_need_a_draft()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var url = $"/api/bills/{created.Id}/receipts";

        for (var i = 2; i <= 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { source = "sample" })).StatusCode);
        var eleventh = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { source = "sample" });
        var noDraft = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { source = "manual" });

        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
        Assert.Contains("too-many-receipts", await eleventh.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, noDraft.StatusCode);
    }

    [Fact]
    public async Task Replace_needs_If_Match_and_refuses_a_stale_version_with_the_latest_bill()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var receipt = (await client.GetBillAsync(created.Id)).Bill.Receipts[0];
        var url = $"/api/bills/{created.Id}/receipts/{receipt.Id}";
        var draft = DinnerDraft(receipt, receipt.Lines.Select(l => LineOf(l)));

        var missing = await client.OrganizerAsync(HttpMethod.Put, url, created.OrganizerKey, draft);
        await client.PatchAsync(created.Id, new { op = "join", name = "Zed" });
        var stale = await client.OrganizerAsync(HttpMethod.Put, url, created.OrganizerKey, draft, ifMatch: "\"1\"");
        var current = await client.OrganizerAsync(HttpMethod.Put, url, created.OrganizerKey, draft, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(2, (await stale.ReadAsync<ConflictBody>())!.Bill.Version);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task Editing_keeps_claims_on_units_that_remain_and_tells_people_whose_claims_went()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        await client.ClaimSampleAsync(created.Id);
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        var receipt = bill.Receipts[0];

        // Red King goes from 2 to 1 (Jo's unit goes) and Tamago is deleted (Kai's claim goes).
        var lines = receipt.Lines
            .Where(l => l.Name != "Tamago")
            .Select(l => LineOf(l, l.Name == "Red King" ? 1 : null))
            .Append(new { id = (string?)null, name = "Iced tea", quantity = 1, unitPrice = 10000L });
        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/receipts/{receipt.Id}",
            created.OrganizerKey, DinnerDraft(receipt, lines, confirmedTotal: 467580), ApiETag(bill));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (after, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Equal(after.PersonId("Ben"), after.Unit(bill.UnitId("Butao King", 2)).Claims[0].PersonId);
        Assert.Equal(3, after.Unit(bill.UnitId("Classic Gyoza")).Claims.Count);
        Assert.Contains(after.Activity, a => a.Kind == ActivityKind.ClaimRemoved && a.Text == "Jo's claim on Red King was removed");
        Assert.Contains(after.Activity, a => a.Kind == ActivityKind.ClaimRemoved && a.Text == "Kai's claim on Tamago was removed");
        Assert.True(breakdown!.Receipts[0].Matches);
    }

    [Fact]
    public async Task Senior_example_gives_400_and_3_times_560_split_equally()
    {
        using var client = factory.NewClient();
        var (id, key, bill) = await ManualBillAsync(client, ["Lola", "Ana", "Ben", "Carla"], new
        {
            label = "Lunch",
            confirmedTotal = 208000,
            lines = new[] { new { name = "Group meal", quantity = 1, unitPrice = 224000 } },
            adjustments = new[]
            {
                new { kind = "vatExempt", label = "Less VAT, senior share", amount = -6000 },
                new { kind = "senior", label = "SC DISC 20%", amount = -10000 },
            },
        });
        var receipt = bill.Receipts[0];
        var lola = bill.PersonId("Lola");

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{id}/receipts/{receipt.Id}", key, new
        {
            label = "Lunch",
            confirmedTotal = 208000,
            splitMode = "equal",
            lines = receipt.Lines.Select(l => LineOf(l)),
            adjustments = receipt.Adjustments.Select(a => new { id = a.Id, a.Kind, a.Label, a.Amount, scope = new { people = new[] { lola } } }),
            payers = new[] { new { personId = bill.PersonId("Ana"), amount = 208000 } },
        }, ApiETag(bill));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (after, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Equal(40000, breakdown.Owed(after, "Lola"));
        Assert.All(new[] { "Ana", "Ben", "Carla" }, name => Assert.Equal(56000, breakdown.Owed(after, name)));
    }

    [Fact]
    public async Task Two_receipts_net_once_so_Ben_gets_64_26_back_and_sends_nothing()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        await client.ClaimSampleAsync(created.Id);
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        string[] fourOfUs = [bill.PersonId("Ana"), bill.PersonId("Ben"), bill.PersonId("Carla"), bill.PersonId("Dan")];

        var add = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/receipts", created.OrganizerKey, new
        {
            source = "manual",
            draft = new
            {
                label = "Milk tea",
                confirmedTotal = 60000,
                splitMode = "equal",
                lines = new[] { new { name = "Milk tea", quantity = 4, unitPrice = 15000 } },
                participants = fourOfUs,
                payers = new[] { new { personId = bill.PersonId("Ben"), amount = 60000 } },
            },
        });

        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var (after, breakdown) = (await add.ReadAsync<BillResponse>())!;
        var ben = breakdown!.People.Single(p => p.PersonId == after.PersonId("Ben"));
        Assert.Equal(6426, ben.Net);
        Assert.Equal(470106, breakdown.People.Single(p => p.PersonId == after.PersonId("Ana")).Net);
        Assert.Equal(11, breakdown.Transfers.Count);
        Assert.DoesNotContain(breakdown.Transfers, t => t.FromPersonId == ben.PersonId);
        Assert.Equal(6426, Assert.Single(breakdown.Transfers, t => t.ToPersonId == ben.PersonId).Amount);
    }

    [Fact]
    public async Task Shares_take_halves_and_refuse_finer_weights()
    {
        using var client = factory.NewClient();
        var (id, key, bill) = await ManualBillAsync(client, ["Ana", "Ben", "Kid"], new
        {
            label = "Pizza",
            confirmedTotal = 100000,
            lines = new[] { new { name = "Pizza", quantity = 1, unitPrice = 100000 } },
        });
        var url = $"/api/bills/{id}/receipts/{bill.Receipts[0].Id}";
        object Draft(decimal kid) => new
        {
            label = "Pizza",
            confirmedTotal = 100000,
            splitMode = "shares",
            lines = bill.Receipts[0].Lines.Select(l => LineOf(l)),
            shares = new Dictionary<string, decimal> { [bill.PersonId("Kid")] = kid },
        };

        var fine = await client.OrganizerAsync(HttpMethod.Put, url, key, Draft(0.333m), ApiETag(bill));
        var half = await client.OrganizerAsync(HttpMethod.Put, url, key, Draft(0.5m), ApiETag(bill));

        Assert.Equal(HttpStatusCode.BadRequest, fine.StatusCode);
        var (after, breakdown) = (await half.ReadAsync<BillResponse>())!;
        Assert.Equal(40000, breakdown.Owed(after, "Ana"));
        Assert.Equal(40000, breakdown.Owed(after, "Ben"));
        Assert.Equal(20000, breakdown.Owed(after, "Kid"));
    }

    public static TheoryData<string> BadReplaces() => new()
    {
        "unknown-line", "unknown-unit", "unknown-person", "duplicate-participant", "payer-amount", "share",
    };

    [Theory]
    [MemberData(nameof(BadReplaces))]
    public async Task Replace_refuses_references_outside_this_bill_and_receipt(string reason)
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var other = (await client.GetBillAsync((await client.CreateSampleAsync()).Id)).Bill;
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        var receipt = bill.Receipts[0];
        var ana = bill.PersonId("Ana");

        object body = reason switch
        {
            "unknown-line" => new { label = "x", confirmedTotal = 0, lines = new[] { new { id = other.Receipts[0].Lines[0].Id, name = "x", quantity = 1, unitPrice = 0 } } },
            "unknown-unit" => new { label = "x", confirmedTotal = 0, lines = Array.Empty<object>(), adjustments = new[] { new { kind = "fee", label = "x", amount = 1, scope = new { units = new[] { other.UnitId("Tamago") } } } } },
            "unknown-person" => new { label = "x", confirmedTotal = 0, lines = Array.Empty<object>(), participants = new[] { other.PersonId("Ana") } },
            "duplicate-participant" => new { label = "x", confirmedTotal = 0, lines = Array.Empty<object>(), participants = new[] { ana, ana } },
            "payer-amount" => new { label = "x", confirmedTotal = 0, lines = Array.Empty<object>(), payers = new[] { new { personId = ana, amount = -1 } } },
            _ => new { label = "x", confirmedTotal = 0, lines = Array.Empty<object>(), shares = new Dictionary<string, decimal> { [ana] = 0 } },
        };

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/receipts/{receipt.Id}", created.OrganizerKey, body, "\"1\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, (await client.GetBillAsync(created.Id)).Bill.Version);
    }

    [Fact]
    public async Task Changing_the_confirmed_total_of_a_shared_bill_moves_it_back_to_draft()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/share", created.OrganizerKey);
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        Assert.Equal(BillStatus.Shared, bill.Status);
        var receipt = bill.Receipts[0];
        var url = $"/api/bills/{created.Id}/receipts/{receipt.Id}";

        var wrong = await client.OrganizerAsync(HttpMethod.Put, url, created.OrganizerKey,
            DinnerDraft(receipt, receipt.Lines.Select(l => LineOf(l)), confirmedTotal: 525581), ApiETag(bill));
        var (afterWrong, breakdown) = (await wrong.ReadAsync<BillResponse>())!;

        Assert.Equal(BillStatus.Draft, afterWrong.Status);
        Assert.False(breakdown!.CanShare);
        Assert.Equal(-1, breakdown.Receipts[0].Difference);
    }

    [Fact]
    public async Task Deleting_a_receipt_removes_its_claims_with_a_notice()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        await client.ClaimAsync(created.Id, bill.UnitId("Tamago"), bill.PersonId("Kai"));
        bill = (await client.GetBillAsync(created.Id)).Bill;

        var response = await client.OrganizerAsync(HttpMethod.Delete, $"/api/bills/{created.Id}/receipts/{bill.Receipts[0].Id}", created.OrganizerKey, ifMatch: ApiETag(bill));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (after, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Empty(after.Receipts);
        Assert.Contains(after.Activity, a => a.Text == "Kai's claim on Tamago was removed");
        Assert.False(breakdown!.CanShare);
    }

    private static string ApiETag(BillView bill) => $"\"{bill.Version}\"";

    private static async Task<(string Id, string Key, BillView Bill)> ManualBillAsync(HttpClient client, string[] names, object draft)
    {
        var response = await client.PostAsJsonAsync("/api/bills", new { source = "manual", organizerName = names[0], currency = new { code = "PHP", digits = 2 }, draft });
        var created = (await response.ReadAsync<Created>())!;
        foreach (var name in names.Skip(1))
            await client.PatchAsync(created.Id, new { op = "addPerson", name });
        return (created.Id, created.OrganizerKey, (await client.GetBillAsync(created.Id)).Bill);
    }
}
