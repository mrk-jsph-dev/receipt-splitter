using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Settings;

public class UpdateSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Sets_the_title_and_a_multi_line_payment_note()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/settings", created.OrganizerKey,
            new { title = " Team lunch ", currency = new { code = "PHP", digits = 2 }, paymentNote = "GCash 0917 000 0000\nBPI 1234" }, "\"1\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bill = (await response.ReadAsync<BillResponse>())!.Bill;
        Assert.Equal("Team lunch", bill.Title);
        Assert.Equal("GCash 0917 000 0000\nBPI 1234", bill.PaymentNote);
    }

    [Fact]
    public async Task Needs_If_Match()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/settings", created.OrganizerKey,
            new { title = "x", currency = new { code = "PHP", digits = 2 } });

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Currency_changes_only_while_there_are_no_receipts()
    {
        using var client = factory.NewClient();
        var sample = await client.CreateSampleAsync();
        var empty = (await (await client.PostAsJsonAsync("/api/bills", new { source = "manual", currency = new { code = "PHP", digits = 2 } })).ReadAsync<Created>())!;
        var yen = new { title = "Tokyo", currency = new { code = "JPY", digits = 0 } };

        var withReceipts = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{sample.Id}/settings", sample.OrganizerKey, yen, "\"1\"");
        var withoutReceipts = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{empty.Id}/settings", empty.OrganizerKey, yen, "\"1\"");

        Assert.Equal(HttpStatusCode.BadRequest, withReceipts.StatusCode);
        Assert.Contains("currency-has-receipts", await withReceipts.Content.ReadAsStringAsync());
        Assert.Equal(new CurrencyInfo("JPY", 0), (await withoutReceipts.ReadAsync<BillResponse>())!.Bill.Currency);
    }

    [Theory]
    [InlineData("", "PHP", 2, null)]
    [InlineData("x", "PESO", 2, null)]
    [InlineData("x", "PHP", 2, "tab\tin note")]
    public async Task Bad_settings_are_400(string title, string code, int digits, string? note)
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/settings", created.OrganizerKey,
            new { title, currency = new { code, digits }, paymentNote = note }, "\"1\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_payment_note_over_280_characters_is_refused()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Put, $"/api/bills/{created.Id}/settings", created.OrganizerKey,
            new { title = "x", currency = new { code = "PHP", digits = 2 }, paymentNote = new string('a', 281) }, "\"1\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
