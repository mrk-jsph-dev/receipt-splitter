using System.Net;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Tests.Features.PatchBill;

public class PatchBillTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, string Id, BillView Bill)> SampleAsync()
    {
        var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        return (client, created.Id, (await client.GetBillAsync(created.Id)).Bill);
    }

    [Fact]
    public async Task Join_adds_the_person_last_with_an_activity_entry_and_returns_the_new_bill()
    {
        var (client, id, _) = await SampleAsync();

        var response = await client.PatchAsync(id, new { op = "join", name = " Zed " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag!.Tag);
        var (bill, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Equal("Zed", bill.People[^1].Name);
        Assert.Equal(13, breakdown!.People.Count);
        var entry = Assert.Single(bill.Activity);
        Assert.Equal(ActivityKind.Join, entry.Kind);
        Assert.Equal("Zed joined", entry.Text);
        Assert.Equal(bill.People[^1].Id, entry.PersonId);
    }

    [Fact]
    public async Task A_name_already_on_the_bill_is_a_409_with_the_latest_bill()
    {
        var (client, id, _) = await SampleAsync();

        var response = await client.PatchAsync(id, new { op = "addPerson", name = "BEN" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = (await response.ReadAsync<ConflictBody>())!;
        Assert.Equal("name-taken", body.Reason);
        Assert.Equal(12, body.Bill.People.Count);
        Assert.Equal(1, body.Bill.Version);
    }

    [Fact]
    public async Task Claiming_with_a_stale_from_is_refused_and_shows_who_claimed_first()
    {
        var (client, id, bill) = await SampleAsync();
        var kakuni = bill.UnitId("Kakuni Belly");
        await client.ClaimAsync(id, kakuni, bill.PersonId("Carla"));

        var response = await client.PatchAsync(id, new { op = "claim", unitId = kakuni, personId = bill.PersonId("Ben"), from = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = (await response.ReadAsync<ConflictBody>())!;
        Assert.Equal("changed", body.Reason);
        Assert.Equal(bill.PersonId("Carla"), Assert.Single(body.Bill.Unit(kakuni).Claims).PersonId);
        Assert.Equal("Carla claimed Kakuni Belly", body.Bill.Activity[^1].Text);
    }

    [Fact]
    public async Task Two_people_claiming_the_same_unit_at_once_gives_one_winner()
    {
        var (_, id, bill) = await SampleAsync();
        var unit = bill.UnitId("Tamago");
        using var ben = factory.NewClient();
        using var carla = factory.NewClient();

        var responses = await Task.WhenAll(
            ben.PatchAsync(id, new { op = "claim", unitId = unit, personId = bill.PersonId("Ben"), from = Array.Empty<object>() }),
            carla.PatchAsync(id, new { op = "claim", unitId = unit, personId = bill.PersonId("Carla"), from = Array.Empty<object>() }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await ben.GetBillAsync(id)).Bill.Unit(unit).Claims);
    }

    [Fact]
    public async Task Weights_2_1_1_split_a_shared_unit_and_the_breakdown_follows()
    {
        var (client, id, bill) = await SampleAsync();
        var gyoza = bill.UnitId("Classic Gyoza");
        await client.ClaimAsync(id, gyoza, bill.PersonId("Ben"));
        await client.ClaimAsync(id, gyoza, bill.PersonId("Fay"));
        await client.ClaimAsync(id, gyoza, bill.PersonId("Gio"));

        var current = (await client.GetBillAsync(id)).Bill.Unit(gyoza).Claims;
        var response = await client.PatchAsync(id, new { op = "setWeight", unitId = gyoza, personId = bill.PersonId("Ben"), weight = 2, from = current });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (after, breakdown) = (await response.ReadAsync<BillResponse>())!;
        var shares = breakdown!.Receipts[0].People;
        Assert.Equal(9500, shares.Single(p => p.PersonId == after.PersonId("Ben")).Subtotal);
        Assert.Equal(4750, shares.Single(p => p.PersonId == after.PersonId("Fay")).Subtotal);
        Assert.Equal(4750, shares.Single(p => p.PersonId == after.PersonId("Gio")).Subtotal);
    }

    [Fact]
    public async Task Unclaim_returns_the_unit_to_unclaimed()
    {
        var (client, id, bill) = await SampleAsync();
        var tamago = bill.UnitId("Tamago");
        await client.ClaimAsync(id, tamago, bill.PersonId("Kai"));

        var response = await client.PatchAsync(id, new { op = "unclaim", unitId = tamago, personId = bill.PersonId("Kai"), from = new[] { new { personId = bill.PersonId("Kai"), weight = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (after, breakdown) = (await response.ReadAsync<BillResponse>())!;
        Assert.Empty(after.Unit(tamago).Claims);
        Assert.Equal(762500, breakdown!.Receipts[0].Unclaimed.Subtotal);
    }

    [Fact]
    public async Task Rename_checks_what_the_client_saw()
    {
        var (client, id, bill) = await SampleAsync();
        var ben = bill.PersonId("Ben");

        var stale = await client.PatchAsync(id, new { op = "rename", personId = ben, name = "Benny", from = "Benjamin" });
        var taken = await client.PatchAsync(id, new { op = "rename", personId = ben, name = "carla", from = "Ben" });
        var ok = await client.PatchAsync(id, new { op = "rename", personId = ben, name = "Benny", from = "Ben" });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Benny", (await client.GetBillAsync(id)).Bill.People[1].Name);
    }

    [Fact]
    public async Task Only_people_the_bill_does_not_refer_to_can_be_removed()
    {
        var (client, id, bill) = await SampleAsync();
        await client.ClaimAsync(id, bill.UnitId("Tamago"), bill.PersonId("Kai"));

        var payer = await client.PatchAsync(id, new { op = "removePerson", personId = bill.PersonId("Ana"), from = "Ana" });
        var claimer = await client.PatchAsync(id, new { op = "removePerson", personId = bill.PersonId("Kai"), from = "Kai" });
        var free = await client.PatchAsync(id, new { op = "removePerson", personId = bill.PersonId("Lia"), from = "Lia" });

        Assert.Equal("person-in-use", (await payer.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal("person-in-use", (await claimer.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal(HttpStatusCode.OK, free.StatusCode);
        Assert.DoesNotContain((await client.GetBillAsync(id)).Bill.People, p => p.Name == "Lia");
    }

    [Fact]
    public async Task Mark_paid_sets_waiting_once()
    {
        var (client, id, bill) = await SampleAsync();
        var ben = bill.PersonId("Ben");

        var first = await client.PatchAsync(id, new { op = "markPaid", personId = ben, from = (string?)null });
        var stale = await client.PatchAsync(id, new { op = "markPaid", personId = ben, from = (string?)null });
        var again = await client.PatchAsync(id, new { op = "markPaid", personId = ben, from = "waiting" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var payment = Assert.Single((await first.ReadAsync<BillResponse>())!.Bill.Payments);
        Assert.Equal(PaymentStatus.Waiting, payment.Status);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task A_locked_bill_takes_no_ops()
    {
        var (client, id, bill) = await SampleAsync();
        await Change(id, b => b.Status = BillStatus.Locked);

        var response = await client.PatchAsync(id, new { op = "join", name = "Zed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("locked", (await response.ReadAsync<ConflictBody>())!.Reason);
    }

    [Fact]
    public async Task Claims_only_work_on_receipts_split_by_item_and_by_people_who_were_there()
    {
        var (client, id, bill) = await SampleAsync();
        await Change(id, b => b.Receipts[0].Participants = [b.People[0].Id, b.People[1].Id]);

        var notThere = await client.ClaimAsync(id, bill.UnitId("Tamago"), bill.PersonId("Kai"));
        await Change(id, b => b.Receipts[0].SplitMode = SplitMode.Equal);
        var equal = await client.ClaimAsync(id, bill.UnitId("Tamago"), bill.PersonId("Ben"));

        Assert.Equal(HttpStatusCode.BadRequest, notThere.StatusCode);
        Assert.Contains("not-participant", await notThere.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, equal.StatusCode);
        Assert.Contains("not-by-item", await equal.Content.ReadAsStringAsync());
    }

    public static TheoryData<string> BadOps() => new()
    {
        """{}""",
        """{ "op": "steal" }""",
        """{ "op": "join" }""",
        """{ "op": "join", "name": "" }""",
        """{ "op": "join", "name": "Zed\u0000" }""",
        """{ "op": "claim", "unitId": "x", "personId": "y" }""",
        """{ "op": "rename", "personId": "y", "name": "Zed" }""",
        """[1, 2]""",
    };

    [Theory]
    [MemberData(nameof(BadOps))]
    public async Task Malformed_ops_are_400_and_change_nothing(string body)
    {
        var (client, id, _) = await SampleAsync();

        var response = await client.PatchAsync($"/api/bills/{id}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, (await client.GetBillAsync(id)).Bill.Version);
    }

    [Fact]
    public async Task Weights_outside_1_to_100_are_refused()
    {
        var (client, id, bill) = await SampleAsync();

        var zero = await client.ClaimAsync(id, bill.UnitId("Tamago"), bill.PersonId("Kai"), weight: 0);
        var huge = await client.ClaimAsync(id, bill.UnitId("Tamago"), bill.PersonId("Kai"), weight: 101);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
    }

    private async Task Change(string id, Action<BillDocument> change)
    {
        var stored = (await factory.Store.GetAsync(id, default))!;
        change(stored.Bill);
        Assert.True(await factory.Store.ReplaceAsync(stored.Bill, stored.ETag, default));
    }
}
