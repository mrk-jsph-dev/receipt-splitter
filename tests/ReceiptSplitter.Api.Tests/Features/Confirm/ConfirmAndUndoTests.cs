using System.Net;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Confirm;

public class ConfirmAndUndoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_organizer_confirms_a_waiting_payment_once()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var ben = (await client.GetBillAsync(created.Id)).Bill.PersonId("Ben");
        await client.PatchAsync(created.Id, new { op = "markPaid", personId = ben, from = (string?)null });
        var url = $"/api/bills/{created.Id}/confirm";

        var confirm = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { personId = ben });
        var again = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { personId = ben });

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var bill = (await confirm.ReadAsync<BillResponse>())!.Bill;
        Assert.Equal(PaymentStatus.Confirmed, Assert.Single(bill.Payments).Status);
        Assert.Equal("Ben's payment was confirmed", bill.Activity[^1].Text);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task A_cash_payment_can_be_confirmed_without_I_have_paid()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var dan = (await client.GetBillAsync(created.Id)).Bill.PersonId("Dan");

        var response = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/confirm", created.OrganizerKey, new { personId = dan });

        Assert.Equal(PaymentStatus.Confirmed, Assert.Single((await response.ReadAsync<BillResponse>())!.Bill.Payments).Status);
    }

    [Fact]
    public async Task Undo_takes_back_a_claim_by_its_activity_entry_once()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var bill = (await client.GetBillAsync(created.Id)).Bill;
        var kakuni = bill.UnitId("Kakuni Belly");
        await client.ClaimAsync(created.Id, kakuni, bill.PersonId("Ben"));
        await client.PatchAsync(created.Id, new { op = "join", name = "Zed" });
        bill = (await client.GetBillAsync(created.Id)).Bill;
        var claimEntry = bill.Activity.Single(a => a.Kind == ActivityKind.Claim).Id;
        var joinEntry = bill.Activity.Single(a => a.Kind == ActivityKind.Join).Id;
        var url = $"/api/bills/{created.Id}/undo";

        var undo = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { activityId = claimEntry });
        var again = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { activityId = claimEntry });
        var notAClaim = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { activityId = joinEntry });

        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var after = (await undo.ReadAsync<BillResponse>())!.Bill;
        Assert.Empty(after.Unit(kakuni).Claims);
        Assert.Equal("Ben's claim on Kakuni Belly was undone", after.Activity[^1].Text);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-undone", (await again.ReadAsync<ConflictBody>())!.Reason);
        Assert.Equal(HttpStatusCode.BadRequest, notAClaim.StatusCode);
    }

    [Fact]
    public async Task Not_yet_sends_a_payment_back_to_unpaid()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var ben = (await client.GetBillAsync(created.Id)).Bill.PersonId("Ben");
        await client.PatchAsync(created.Id, new { op = "markPaid", personId = ben, from = (string?)null });
        var url = $"/api/bills/{created.Id}/decline";

        var noKey = await client.OrganizerAsync(HttpMethod.Post, url, null, new { personId = ben });
        var decline = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { personId = ben });
        var again = await client.OrganizerAsync(HttpMethod.Post, url, created.OrganizerKey, new { personId = ben });

        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        var bill = (await decline.ReadAsync<BillResponse>())!.Bill;
        Assert.Empty(bill.Payments);
        Assert.Equal("Ben's payment hasn't arrived yet", bill.Activity[^1].Text);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}
