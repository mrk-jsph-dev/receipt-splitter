using System.Net;
using ReceiptSplitter.Api.Infrastructure.Security;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Security;

/// <summary>The organizer key is needed to edit, confirm, undo, lock or delete.</summary>
public class OrganizerAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string> Routes() => new()
    {
        "add-receipt", "replace-receipt", "delete-receipt", "settings", "share", "lock", "confirm", "undo", "delete-bill",
    };

    private static async Task<HttpResponseMessage> Call(HttpClient client, string route, string id, string? key, string pathSuffix = "")
    {
        var bill = (await client.GetBillAsync(id)).Bill;
        var receipt = bill.Receipts[0];
        var url = $"/api/bills/{id}";
        var draft = new { label = "Edited", confirmedTotal = 1, lines = new[] { new { name = "x", quantity = 1, unitPrice = 1 } } };
        return route switch
        {
            "add-receipt" => await client.OrganizerAsync(HttpMethod.Post, $"{url}/receipts{pathSuffix}", key, new { source = "sample" }),
            "replace-receipt" => await client.OrganizerAsync(HttpMethod.Put, $"{url}/receipts/{receipt.Id}{pathSuffix}", key, draft, "\"1\""),
            "delete-receipt" => await client.OrganizerAsync(HttpMethod.Delete, $"{url}/receipts/{receipt.Id}{pathSuffix}", key, ifMatch: "\"1\""),
            "settings" => await client.OrganizerAsync(HttpMethod.Put, $"{url}/settings{pathSuffix}", key, new { title = "Mine now", currency = new { code = "PHP", digits = 2 } }, "\"1\""),
            "share" => await client.OrganizerAsync(HttpMethod.Post, $"{url}/share{pathSuffix}", key),
            "lock" => await client.OrganizerAsync(HttpMethod.Post, $"{url}/lock{pathSuffix}", key),
            "confirm" => await client.OrganizerAsync(HttpMethod.Post, $"{url}/confirm{pathSuffix}", key, new { personId = bill.PersonId("Ben") }),
            "undo" => await client.OrganizerAsync(HttpMethod.Post, $"{url}/undo{pathSuffix}", key, new { activityId = "nothing" }),
            "delete-bill" => await client.OrganizerAsync(HttpMethod.Delete, $"{url}{pathSuffix}", key),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Without_the_right_key_every_organizer_call_is_403_and_changes_nothing(string route)
    {
        using var client = factory.NewClient();
        var a = await client.CreateSampleAsync();
        var b = await client.CreateSampleAsync();
        var before = await client.GetStringAsync($"/api/bills/{a.Id}");

        HttpResponseMessage[] attempts =
        [
            await Call(client, route, a.Id, key: null),
            await Call(client, route, a.Id, key: ""),
            await Call(client, route, a.Id, key: SecureIds.NewSecret()),
            await Call(client, route, a.Id, key: b.OrganizerKey),
            await Call(client, route, a.Id, key: OrganizerKey.Hash(a.OrganizerKey)),
            await Call(client, route, a.Id, key: null, pathSuffix: $"?key={a.OrganizerKey}"),
            await Call(client, route, a.Id, key: null, pathSuffix: $"?organizerKey={a.OrganizerKey}"),
        ];

        Assert.All(attempts, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(before, await client.GetStringAsync($"/api/bills/{a.Id}"));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task With_the_key_every_organizer_call_gets_past_the_key_check(string route)
    {
        using var client = factory.NewClient();
        var a = await client.CreateSampleAsync();

        var response = await Call(client, route, a.Id, a.OrganizerKey);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_deleted_bill_gives_the_same_404_as_one_that_never_existed()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var delete = await client.OrganizerAsync(HttpMethod.Delete, $"/api/bills/{created.Id}", created.OrganizerKey);
        var gone = await client.GetAsync($"/api/bills/{created.Id}");
        var never = await client.GetAsync($"/api/bills/{SecureIds.NewSecret()}");
        var deleteAgain = await client.OrganizerAsync(HttpMethod.Delete, $"/api/bills/{created.Id}", created.OrganizerKey);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(await never.Content.ReadAsStringAsync(), await gone.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
        Assert.Null(await factory.Store.GetAsync(created.Id, default));
    }
}
