using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Security;

/// <summary>
/// Friends change only their own part: their claims, their payment, their name and card. Joining or
/// picking a name gives the phone a person token for it. The organizer key can do everything,
/// including freeing a name and reopening a locked bill.
/// </summary>
public class OwnPartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TokenHeader = "X-Person-Token";

    private static async Task<(BillView Bill, string Token)> PickAsync(HttpClient client, string id, string name)
    {
        var bill = (await client.GetBillAsync(id)).Bill;
        var response = await client.FriendPatchAsync(id, new { op = "pick", personId = bill.PersonId(name) }, null);
        response.EnsureSuccessStatusCode();
        return (bill, response.Headers.GetValues(TokenHeader).Single());
    }

    private static object Claim(BillView bill, string line, string person) =>
        new { op = "claim", unitId = bill.UnitId(line), personId = bill.PersonId(person), weight = 1, from = Array.Empty<object>() };

    private static async Task<IReadOnlyList<ClaimDoc>> ClaimsOn(HttpClient client, string id, string line)
    {
        var bill = (await client.GetBillAsync(id)).Bill;
        return bill.Unit(bill.UnitId(line)).Claims;
    }

    [Fact]
    public async Task A_friend_claims_for_themselves_but_not_for_someone_else()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, ana) = await PickAsync(client, created.Id, "Ana");

        var own = await client.FriendPatchAsync(created.Id, Claim(bill, "Tamago", "Ana"), ana);
        var other = await client.FriendPatchAsync(created.Id, Claim(bill, "Coke Zero", "Ben"), ana);
        var none = await client.FriendPatchAsync(created.Id, Claim(bill, "Coke Zero", "Ben"), null);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.Empty(await ClaimsOn(client, created.Id, "Coke Zero"));
    }

    [Fact]
    public async Task A_friend_marks_only_their_own_payment_and_card_and_name()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, ana) = await PickAsync(client, created.Id, "Ana");

        Assert.Equal(HttpStatusCode.OK, (await client.FriendPatchAsync(created.Id, new { op = "markPaid", personId = bill.PersonId("Ana"), from = (string?)null }, ana)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.FriendPatchAsync(created.Id, new { op = "markPaid", personId = bill.PersonId("Ben"), from = (string?)null }, ana)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.FriendPatchAsync(created.Id, new { op = "setCard", personId = bill.PersonId("Ben"), card = "senior", from = "none" }, ana)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.FriendPatchAsync(created.Id, new { op = "rename", personId = bill.PersonId("Ben"), name = "Benny", from = "Ben" }, ana)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.FriendPatchAsync(created.Id, new { op = "rename", personId = bill.PersonId("Ana"), name = "Annie", from = "Ana" }, ana)).StatusCode);
    }

    [Fact]
    public async Task Only_the_organizer_adds_or_removes_people_and_does_anything_for_anyone()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, ana) = await PickAsync(client, created.Id, "Ana");

        var friendAdds = await client.FriendPatchAsync(created.Id, new { op = "addPerson", name = "Zed" }, ana);
        var friendRemoves = await client.FriendPatchAsync(created.Id, new { op = "removePerson", personId = bill.PersonId("Lia"), from = "Lia" }, ana);
        var organizerClaims = await client.PatchAsync(created.Id, Claim(bill, "Coke Zero", "Ben"));
        var organizerAdds = await client.PatchAsync(created.Id, new { op = "addPerson", name = "Zed" });

        Assert.Equal(HttpStatusCode.Forbidden, friendAdds.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, friendRemoves.StatusCode);
        Assert.Equal(HttpStatusCode.OK, organizerClaims.StatusCode);
        Assert.Equal(HttpStatusCode.OK, organizerAdds.StatusCode);
    }

    [Fact]
    public async Task Joining_gives_the_new_person_a_token()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var join = await client.FriendPatchAsync(created.Id, new { op = "join", name = "Zed" }, null);
        var token = join.Headers.GetValues(TokenHeader).Single();
        var bill = (await join.ReadAsync<BillResponse>())!.Bill;
        var claim = await client.FriendPatchAsync(created.Id, Claim(bill, "Tamago", "Zed"), token);

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.True(bill.People.Single(p => p.Name == "Zed").Held);
    }

    [Fact]
    public async Task A_name_is_held_by_one_phone_until_it_is_freed()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, first) = await PickAsync(client, created.Id, "Ana");

        var second = await client.FriendPatchAsync(created.Id, new { op = "pick", personId = bill.PersonId("Ana") }, null);
        var reset = await client.PatchAsync(created.Id, new { op = "release", personId = bill.PersonId("Ana") });
        var oldToken = await client.FriendPatchAsync(created.Id, Claim(bill, "Tamago", "Ana"), first);
        var again = await client.FriendPatchAsync(created.Id, new { op = "pick", personId = bill.PersonId("Ana") }, null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("name-held", (await second.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, oldToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_friend_can_let_go_of_their_own_name_but_not_someone_elses()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, ana) = await PickAsync(client, created.Id, "Ana");
        await PickAsync(client, created.Id, "Ben");

        var theirs = await client.FriendPatchAsync(created.Id, new { op = "release", personId = bill.PersonId("Ben") }, ana);
        var own = await client.FriendPatchAsync(created.Id, new { op = "release", personId = bill.PersonId("Ana") }, ana);

        Assert.Equal(HttpStatusCode.Forbidden, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.False((await own.ReadAsync<BillResponse>())!.Bill.People.Single(p => p.Name == "Ana").Held);
    }

    [Fact]
    public async Task The_organizers_own_name_is_held_from_the_start()
    {
        using var client = factory.NewClient();
        var response = await client.PostAsJsonAsync("/api/bills", new { source = "manual", organizerName = "Ana", currency = new { code = "PHP", digits = 2 } }, ApiJson.Options);
        var created = (await response.ReadAsync<Created>())!;
        var bill = (await client.GetBillAsync(created.Id)).Bill;

        var pick = await client.FriendPatchAsync(created.Id, new { op = "pick", personId = bill.PersonId("Ana") }, null);

        Assert.Equal(HttpStatusCode.Conflict, pick.StatusCode);
    }

    [Fact]
    public async Task Responses_never_carry_the_hold_hash_and_logs_never_carry_tokens()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (_, token) = await PickAsync(client, created.Id, "Ana");

        var raw = await (await client.GetAsync($"/api/bills/{created.Id}")).Content.ReadAsStringAsync();
        var hash = (await factory.Store.GetAsync(created.Id, default))!.Bill.People.Single(p => p.Name == "Ana").HoldHash!;

        Assert.DoesNotContain(hash, raw);
        Assert.DoesNotContain("holdHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(token));
    }

    [Fact]
    public async Task Only_the_organizer_reopens_a_locked_bill_and_claims_work_again()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var (bill, ana) = await PickAsync(client, created.Id, "Ana");
        await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/lock", created.OrganizerKey);

        var lockedClaim = await client.FriendPatchAsync(created.Id, Claim(bill, "Tamago", "Ana"), ana);
        var friendReopens = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/reopen", null);
        var reopen = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/reopen", created.OrganizerKey);
        var claim = await client.FriendPatchAsync(created.Id, Claim(bill, "Tamago", "Ana"), ana);

        Assert.Equal(HttpStatusCode.Conflict, lockedClaim.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, friendReopens.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        var reopened = (await reopen.ReadAsync<BillResponse>())!.Bill;
        Assert.NotEqual(BillStatus.Locked, reopened.Status);
        Assert.Null(reopened.LockedAt);
        Assert.Contains(reopened.Activity, a => a.Text == "The bill was reopened");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
    }
}
