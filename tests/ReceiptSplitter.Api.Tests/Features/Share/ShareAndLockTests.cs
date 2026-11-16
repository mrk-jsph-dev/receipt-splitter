using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Share;

public class ShareAndLockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Sharing_a_matching_bill_works_even_with_items_unclaimed()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var response = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/share", created.OrganizerKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bill = (await response.ReadAsync<BillResponse>())!.Bill;
        Assert.Equal(BillStatus.Shared, bill.Status);
        Assert.Equal("The bill was shared", bill.Activity[^1].Text);
    }

    [Fact]
    public async Task Sharing_is_422_until_every_receipt_matches_its_total()
    {
        using var client = factory.NewClient();
        var empty = (await (await client.PostAsJsonAsync("/api/bills", new { source = "manual", organizerName = "Ana", currency = new { code = "PHP", digits = 2 } })).ReadAsync<Created>())!;
        var mismatch = (await (await client.PostAsJsonAsync("/api/bills", new
        {
            source = "manual",
            organizerName = "Ana",
            currency = new { code = "PHP", digits = 2 },
            draft = new { label = "Lunch", confirmedTotal = 100000, lines = new[] { new { name = "Rice", quantity = 1, unitPrice = 90000 } } },
        })).ReadAsync<Created>())!;

        var noReceipts = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{empty.Id}/share", empty.OrganizerKey);
        var offBy = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{mismatch.Id}/share", mismatch.OrganizerKey);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReceipts.StatusCode);
        Assert.Equal("no-receipts", (await noReceipts.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, offBy.StatusCode);
        var body = (await offBy.ReadAsync<ConflictBody>())!;
        Assert.Equal("total-mismatch", body.Reason);
        Assert.Equal(-10000, body.Breakdown!.Receipts[0].Difference);
        Assert.Equal(BillStatus.Draft, body.Bill.Status);
    }

    [Fact]
    public async Task A_locked_bill_can_be_viewed_and_deleted_but_not_changed()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var url = $"/api/bills/{created.Id}";

        var locked = await client.OrganizerAsync(HttpMethod.Post, $"{url}/lock", created.OrganizerKey);
        var join = await client.PatchAsync(created.Id, new { op = "join", name = "Zed" });
        var share = await client.OrganizerAsync(HttpMethod.Post, $"{url}/share", created.OrganizerKey);
        var lockAgain = await client.OrganizerAsync(HttpMethod.Post, $"{url}/lock", created.OrganizerKey);
        var view = await client.GetBillAsync(created.Id);
        var delete = await client.OrganizerAsync(HttpMethod.Delete, url, created.OrganizerKey);

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(BillStatus.Locked, view.Bill.Status);
        Assert.Equal(factory.Clock.GetUtcNow(), view.Bill.LockedAt);
        Assert.All([join, share, lockAgain], r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal("locked", (await join.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}
