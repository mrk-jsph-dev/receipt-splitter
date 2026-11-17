using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Security;

/// <summary>
/// The optional bill PIN: kept only as a salted hash, traded once for a token the phone keeps,
/// and locked for 15 minutes after 5 wrong tries. The organizer key always gets in.
/// </summary>
public class BillPinTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Pin = "4826";

    private sealed record Unlocked(string Token);
    private sealed record PinRefusal(string Error, string? Reason = null, int? TriesLeft = null);

    private static Task<HttpResponseMessage> SetPinAsync(HttpClient client, Created bill, string? pin, string? key = null) =>
        client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{bill.Id}/pin", key ?? bill.OrganizerKey, new { pin });

    private static Task<HttpResponseMessage> UnlockAsync(HttpClient client, string id, string pin) =>
        client.PostAsJsonAsync($"/api/bills/{id}/pin/unlock", new { pin }, ApiJson.Options);

    private static Task<HttpResponseMessage> GetWithTokenAsync(HttpClient client, string id, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/bills/{id}");
        if (token is not null)
            request.Headers.Add(BillPin.Header, token);
        return client.SendAsync(request);
    }

    private async Task<Created> PinnedBillAsync(HttpClient client)
    {
        var bill = await client.CreateSampleAsync();
        (await SetPinAsync(client, bill, Pin)).EnsureSuccessStatusCode();
        return bill;
    }

    [Fact]
    public async Task The_PIN_is_stored_only_as_a_salted_hash()
    {
        using var client = factory.NewClient();
        var a = await PinnedBillAsync(client);
        var b = await PinnedBillAsync(client);

        var hashA = (await factory.Store.GetAsync(a.Id, default))!.Bill.PinHash!;
        var hashB = (await factory.Store.GetAsync(b.Id, default))!.Bill.PinHash!;
        Assert.StartsWith("pbkdf2-sha256$", hashA);
        Assert.DoesNotContain(Pin, hashA);
        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public async Task Without_the_PIN_a_link_holder_can_neither_read_nor_claim()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);
        var asOrganizer = await client.OrganizerAsync(HttpMethod.Get, $"/api/bills/{created.Id}", created.OrganizerKey);
        var bill = (await asOrganizer.ReadAsync<BillResponse>())!.Bill;

        var read = await GetWithTokenAsync(client, created.Id, null);
        var claim = await client.PatchAsync(created.Id, new { op = "claim", unitId = bill.UnitId("Tamago"), personId = bill.PersonId("Ana"), weight = 1, from = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal("pin-required", (await read.ReadAsync<PinRefusal>())!.Error);
        Assert.Equal(HttpStatusCode.Unauthorized, claim.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asOrganizer.StatusCode);
        Assert.True(bill.HasPin);
        var after = (await (await client.OrganizerAsync(HttpMethod.Get, $"/api/bills/{created.Id}", created.OrganizerKey)).ReadAsync<BillResponse>())!.Bill;
        Assert.Empty(after.Unit(bill.UnitId("Tamago")).Claims);
    }

    [Fact]
    public async Task The_right_PIN_gives_a_token_that_reads_and_claims()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);

        var unlocked = await UnlockAsync(client, created.Id, Pin);
        var token = (await unlocked.ReadAsync<Unlocked>())!.Token;
        var read = await GetWithTokenAsync(client, created.Id, token);
        var bill = (await read.ReadAsync<BillResponse>())!.Bill;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/bills/{created.Id}")
        {
            Content = JsonContent.Create(new { op = "claim", unitId = bill.UnitId("Tamago"), personId = bill.PersonId("Ana"), weight = 1, from = Array.Empty<object>() }, options: ApiJson.Options),
        };
        request.Headers.Add(BillPin.Header, token);
        var claim = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.DoesNotContain(Pin, token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_tries_lock_the_PIN_for_15_minutes_even_from_other_networks()
    {
        var created = await PinnedBillAsync(factory.NewClient());

        var tries = new List<PinRefusal>();
        for (var i = 0; i < 5; i++)
        {
            using var other = factory.NewClient();
            var wrong = await UnlockAsync(other, created.Id, "0000");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            tries.Add((await wrong.ReadAsync<PinRefusal>())!);
        }
        using var client = factory.NewClient();
        var locked = await UnlockAsync(client, created.Id, Pin);
        factory.Clock.Advance(TimeSpan.FromMinutes(15));
        var later = await UnlockAsync(client, created.Id, Pin);

        Assert.Equal([4, 3, 2, 1, 0], tries.Select(t => t.TriesLeft));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("pin-locked", (await locked.ReadAsync<PinRefusal>())!.Reason);
        Assert.True(locked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Fact]
    public async Task Twenty_wrong_tries_in_a_day_lock_it_until_the_next_day()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);

        for (var window = 0; window < 4; window++)
        {
            for (var i = 0; i < 5; i++)
                await UnlockAsync(client, created.Id, "0000");
            factory.Clock.Advance(TimeSpan.FromMinutes(15));
        }
        var sameDay = await UnlockAsync(client, created.Id, Pin);
        factory.Clock.Advance(TimeSpan.FromDays(1));
        var nextDay = await UnlockAsync(client, created.Id, Pin);

        Assert.Equal(HttpStatusCode.TooManyRequests, sameDay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nextDay.StatusCode);
    }

    [Fact]
    public async Task Changing_the_PIN_ends_old_tokens_and_removing_it_opens_the_bill()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);
        var oldToken = (await (await UnlockAsync(client, created.Id, Pin)).ReadAsync<Unlocked>())!.Token;

        (await SetPinAsync(client, created, "135790")).EnsureSuccessStatusCode();
        var withOld = await GetWithTokenAsync(client, created.Id, oldToken);
        (await SetPinAsync(client, created, null)).EnsureSuccessStatusCode();
        var open = await GetWithTokenAsync(client, created.Id, null);

        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        Assert.False((await open.ReadAsync<BillResponse>())!.Bill.HasPin);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("١٢٣٤")]
    [InlineData("")]
    public async Task A_PIN_is_4_to_6_digits(string pin)
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await SetPinAsync(client, created, pin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_organizer_can_set_a_PIN()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var other = await client.CreateSampleAsync();

        var noKey = await SetPinAsync(client, created, Pin, key: "");
        var wrongKey = await SetPinAsync(client, created, Pin, key: other.OrganizerKey);

        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        Assert.False((await client.GetBillAsync(created.Id)).Bill.HasPin);
    }

    [Fact]
    public async Task Unknown_bills_give_the_same_404_with_or_without_a_token()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);
        var token = (await (await UnlockAsync(client, created.Id, Pin)).ReadAsync<Unlocked>())!.Token;

        var missing = await GetWithTokenAsync(client, SecureIds.NewSecret(), token);
        var unlockMissing = await UnlockAsync(client, SecureIds.NewSecret(), Pin);
        var plain = await client.GetAsync($"/api/bills/{SecureIds.NewSecret()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlockMissing.StatusCode);
        Assert.Equal(await plain.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Equal(await plain.Content.ReadAsStringAsync(), await unlockMissing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_PIN_and_token_never_reach_the_logs_or_the_activity_list()
    {
        using var client = factory.NewClient();
        var created = await PinnedBillAsync(client);
        await UnlockAsync(client, created.Id, "9999");
        var token = (await (await UnlockAsync(client, created.Id, Pin)).ReadAsync<Unlocked>())!.Token;
        await GetWithTokenAsync(client, created.Id, token);

        var bill = (await (await GetWithTokenAsync(client, created.Id, token)).ReadAsync<BillResponse>())!.Bill;

        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(Pin) || line.Contains("9999") || line.Contains(token));
        Assert.Contains(bill.Activity, a => a.Text == "A PIN was added");
        Assert.DoesNotContain(bill.Activity, a => a.Text.Contains(Pin));
    }
}
