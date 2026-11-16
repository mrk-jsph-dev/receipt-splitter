using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.GetBill;

public class ProgressTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Progress_gives_the_bill_total_and_the_confirmed_share_of_people_who_owe()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        await client.ClaimLikeWalkthroughAsync(created.Id);
        var bill = (await client.GetBillAsync(created.Id)).Bill;

        // Ana paid the whole dinner, so the other 11 owe her.
        foreach (var name in new[] { "Ben", "Carla" })
            await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/confirm", created.OrganizerKey, new { personId = bill.PersonId(name) });
        var progress = (await client.GetBillAsync(created.Id)).Breakdown!.Progress;

        Assert.Equal(new BillProgress(525580, 11, 2, 18), progress);
    }

    [Fact]
    public async Task A_confirmed_payment_from_someone_who_is_owed_money_does_not_count()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        var ana = (await client.GetBillAsync(created.Id)).Bill.PersonId("Ana");

        var response = await client.OrganizerAsync(HttpMethod.Post, $"/api/bills/{created.Id}/confirm", created.OrganizerKey, new { personId = ana });

        var progress = (await response.ReadAsync<BillResponse>())!.Breakdown!.Progress;
        Assert.Equal(0, progress.Confirmed);
        Assert.Equal(0, progress.PaidPercent);
    }
}
