using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Infrastructure.Security;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Security;

/// <summary>docs/SPEC.md section 9: many bills, nothing leaking between them.</summary>
public class BillIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task An_op_on_bill_A_naming_a_person_or_unit_from_bill_B_is_rejected_and_B_is_unchanged()
    {
        using var client = factory.NewClient();
        var a = await client.CreateSampleAsync();
        var b = await client.CreateSampleAsync();
        var billA = (await client.GetBillAsync(a.Id)).Bill;
        var billB = (await client.GetBillAsync(b.Id)).Bill;
        var bJsonBefore = await client.GetStringAsync($"/api/bills/{b.Id}");

        var foreignUnit = await client.PatchAsync(a.Id, new { op = "claim", unitId = billB.UnitId("Tamago"), personId = billA.PersonId("Ben"), from = Array.Empty<object>() });
        var foreignPerson = await client.PatchAsync(a.Id, new { op = "claim", unitId = billA.UnitId("Tamago"), personId = billB.PersonId("Ben"), from = Array.Empty<object>() });
        var foreignRename = await client.PatchAsync(a.Id, new { op = "rename", personId = billB.PersonId("Ben"), name = "Mallory", from = "Ben" });
        var foreignPaid = await client.PatchAsync(a.Id, new { op = "markPaid", personId = billB.PersonId("Ben"), from = (string?)null });

        Assert.All([foreignUnit, foreignPerson, foreignRename, foreignPaid], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(bJsonBefore, await client.GetStringAsync($"/api/bills/{b.Id}"));
        Assert.Equal(1, (await client.GetBillAsync(a.Id)).Bill.Version);
    }

    [Fact]
    public async Task Unknown_malformed_deleted_and_expired_bills_give_identical_404s()
    {
        using var isolated = new ApiFactory();
        using var client = isolated.NewClient();
        var deleted = await client.CreateSampleAsync();
        await isolated.Store.DeleteAsync(deleted.Id, default);
        var expired = await client.CreateSampleAsync();
        isolated.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1));

        string[] ids = [SecureIds.NewSecret(), "not-a-real-id", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", deleted.Id, expired.Id];
        var answers = new List<string>();
        foreach (var id in ids)
        {
            foreach (var response in new[]
            {
                await client.GetAsync($"/api/bills/{id}"),
                await client.PatchAsync(id, new { op = "join", name = "Zed" }),
            })
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                var headers = response.Headers.Concat(response.Content.Headers)
                    .Where(h => h.Key != "Date")
                    .OrderBy(h => h.Key)
                    .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
                answers.Add(string.Join("\n", headers) + "\n" + await response.Content.ReadAsStringAsync());
            }
        }

        Assert.Single(answers.Distinct());
        Assert.DoesNotContain("ETag", answers[0]);
    }

    [Fact]
    public async Task Past_20_not_found_lookups_in_10_minutes_every_bill_request_from_that_IP_gets_429()
    {
        using var prober = factory.NewClient();
        using var friend = factory.NewClient();
        var real = await friend.CreateSampleAsync();

        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await prober.GetAsync($"/api/bills/{SecureIds.NewSecret()}")).StatusCode);

        var guess = await prober.GetAsync($"/api/bills/{SecureIds.NewSecret()}");
        var hit = await prober.GetAsync($"/api/bills/{real.Id}");
        var write = await prober.PatchAsync(real.Id, new { op = "join", name = "Mallory" });

        Assert.Equal(HttpStatusCode.TooManyRequests, guess.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, hit.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, write.StatusCode);
        Assert.Equal(await guess.Content.ReadAsStringAsync(), await hit.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await friend.GetAsync($"/api/bills/{real.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_not_found_limit_resets_in_the_next_window()
    {
        using var isolated = new ApiFactory();
        using var prober = isolated.NewClient();
        for (var i = 0; i < 21; i++)
            await prober.GetAsync($"/api/bills/{SecureIds.NewSecret()}");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await prober.GetAsync($"/api/bills/{SecureIds.NewSecret()}")).StatusCode);

        isolated.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(HttpStatusCode.NotFound, (await prober.GetAsync($"/api/bills/{SecureIds.NewSecret()}")).StatusCode);
    }

    [Fact]
    public async Task No_response_contains_the_organizer_key_hash()
    {
        using var client = factory.NewClient();
        var created = await client.PostAsJsonAsync("/api/bills", new { source = "sample" });
        var key = (await created.ReadAsync<Created>())!;
        var bill = (await client.GetBillAsync(key.Id)).Bill;
        var hash = OrganizerKey.Hash(key.OrganizerKey);

        HttpResponseMessage[] responses =
        [
            created,
            await client.GetAsync($"/api/bills/{key.Id}"),
            await client.PatchAsync(key.Id, new { op = "join", name = "Zed" }),
            await client.PatchAsync(key.Id, new { op = "join", name = "zed" }),
            await client.PatchAsync(key.Id, new { op = "claim", unitId = bill.UnitId("Tamago"), personId = bill.PersonId("Kai"), from = new[] { new { personId = "x", weight = 1 } } }),
        ];

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("organizerKeyHash", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(hash, body, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(key.OrganizerKey, await created.Content.ReadAsStringAsync());
        Assert.DoesNotContain(key.OrganizerKey, await responses[1].Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/bills")]
    [InlineData("/api/bills/")]
    [InlineData("/api/bills/*")]
    public async Task No_route_lists_bills(string path)
    {
        using var client = factory.NewClient();
        await client.CreateSampleAsync();

        var response = await client.GetAsync(path);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.DoesNotContain("Ramen", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_new_bill_holds_nothing_from_an_earlier_bill_on_the_same_client()
    {
        using var client = factory.NewClient();
        var a = await client.CreateSampleAsync();
        await client.PatchAsync(a.Id, new { op = "join", name = "Zed" });
        var billA = (await client.GetBillAsync(a.Id)).Bill;
        await client.ClaimAsync(a.Id, billA.UnitId("Tamago"), billA.PersonId("Zed"));

        var b = await client.CreateSampleAsync();
        var bJson = await client.GetStringAsync($"/api/bills/{b.Id}");
        var billB = (await client.GetBillAsync(b.Id)).Bill;

        Assert.DoesNotContain("Zed", bJson);
        Assert.Empty(billB.Activity);
        Assert.All(billB.Receipts.SelectMany(r => r.Lines).SelectMany(l => l.Units), u => Assert.Empty(u.Claims));
        var aIds = billA.People.Select(p => p.Id)
            .Concat(billA.Receipts.SelectMany(r => r.Lines).Select(l => l.Id))
            .Append(a.Id);
        Assert.All(aIds, id => Assert.DoesNotContain(id, bJson));
    }

    [Fact]
    public async Task Logs_hold_no_bill_ids_keys_names_or_amounts()
    {
        using var isolated = new ApiFactory();
        using var client = isolated.NewClient();
        var created = await client.CreateSampleAsync();
        await client.PatchAsync(created.Id, new { op = "join", name = "Zelda" });
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        await client.ClaimAsync(created.Id, bill.UnitId("Gyu King"), bill.PersonId("Zelda"));
        await client.PatchAsync(created.Id, new { op = "join", name = "zelda" });
        await client.GetAsync($"/api/bills/{SecureIds.NewSecret()}");
        await client.PostAsJsonAsync("/api/bills", new { source = "manual", currency = new { code = "PHP", digits = 2 }, organizerName = "Quentin" });

        var logs = isolated.Logs.Lines.ToList();
        Assert.Contains(logs, line => line.Contains("Bill ") && line.Contains("saved at version"));
        string[] secrets = [created.Id, created.OrganizerKey, OrganizerKey.Hash(created.OrganizerKey), "Zelda", "Quentin", "Ramen", "Gyu King", "525580", "79500"];
        foreach (var line in logs)
            foreach (var secret in secrets)
                Assert.DoesNotContain(secret, line, StringComparison.OrdinalIgnoreCase);
    }
}
