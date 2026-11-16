using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Bills;

namespace ReceiptSplitter.Api.Tests.Features.CreateBill;

public class CreateBillTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Sample_returns_an_id_and_organizer_key_and_the_bill_has_totals()
    {
        using var client = factory.NewClient();

        var response = await client.PostAsJsonAsync("/api/bills", new { source = "sample" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.ReadAsync<Created>())!;
        Assert.True(SecureIds.IsSecret(created.Id));
        Assert.True(SecureIds.IsSecret(created.OrganizerKey));
        Assert.NotEqual(created.Id, created.OrganizerKey);
        Assert.Equal($"/api/bills/{created.Id}", response.Headers.Location!.OriginalString);

        var (bill, breakdown) = await client.GetBillAsync(created.Id);
        Assert.Equal("Ramen dinner", bill.Title);
        Assert.Equal(new CurrencyInfo("PHP", 2), bill.Currency);
        Assert.Equal(BillStatus.Draft, bill.Status);
        Assert.Equal(12, bill.People.Count);
        Assert.Equal("Ana", bill.People[0].Name);
        Assert.Equal(12, bill.HeadcountHint);
        var receipt = Assert.Single(bill.Receipts);
        Assert.Equal("Ramen Nagi", receipt.Merchant);
        Assert.Equal(ReceiptSource.Sample, receipt.Source);
        Assert.Equal(9, receipt.Lines.Count);
        Assert.Equal(20, receipt.Lines.Sum(l => l.Units.Count));
        Assert.Equal(bill.PersonId("Ana"), Assert.Single(receipt.Payers).PersonId);

        var computed = Assert.Single(breakdown!.Receipts);
        Assert.Equal(525580, computed.ComputedTotal);
        Assert.True(computed.Matches);
        // Nothing claimed yet: all the food and its discount sit in Unclaimed; service is split equally among people.
        Assert.Equal(762500, computed.Unclaimed.Subtotal);
        Assert.Equal(457500, computed.Unclaimed.Total);
        Assert.True(breakdown.CanShare);
        Assert.False(breakdown.CanSettle);
        Assert.Contains(breakdown.Problems, p => p.Code == ProblemCode.UnclaimedItems);
        Assert.Empty(breakdown.Transfers);
    }

    [Fact]
    public async Task Sample_claimed_as_in_the_walkthrough_gives_the_published_numbers()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();
        await client.ClaimLikeWalkthroughAsync(created.Id);

        var (after, breakdown) = await client.GetBillAsync(created.Id);
        Assert.Equal(40474, breakdown.Owed(after, "Ana"));
        Assert.Equal(38574, breakdown.Owed(after, "Ben"));
        Assert.Equal(53374, breakdown.Owed(after, "Carla"));
        Assert.Equal(48274, breakdown.Owed(after, "Dan"));
        Assert.Equal(34773, breakdown.Owed(after, "Eli"));

        var receipt = Assert.Single(breakdown!.Receipts);
        var service = after.Receipts[0].Adjustments.Single(a => a.Label == "SERVICE CHARGE").Id;
        Assert.Equal([5674, 5674, 5674, 5674, 5673, 5673, 5673, 5673, 5673, 5673, 5673, 5673], receipt.People.Select(p => p.Adjustments[service]));
        Assert.Equal(0, receipt.Unclaimed.Total);
        Assert.True(breakdown.CanSettle);
        Assert.Equal(11, breakdown.Transfers.Count);
        Assert.All(breakdown.Transfers, t => Assert.Equal(after.PersonId("Ana"), t.ToPersonId));
        Assert.Equal(22, after.Activity.Count);
    }

    [Fact]
    public async Task Manual_bill_with_a_first_receipt_makes_the_organizer_the_first_person_and_payer()
    {
        using var client = factory.NewClient();

        var response = await client.PostAsJsonAsync("/api/bills", new
        {
            source = "manual",
            organizerName = "  Mika ",
            currency = new { code = "JPY", digits = 0 },
            draft = new
            {
                label = "Lunch",
                merchant = "Tonkatsu Ya",
                confirmedTotal = 3300,
                lines = new object[]
                {
                    new { name = "Rosu katsu", quantity = 2, unitPrice = 1200 },
                    new { name = "Gyoza 3 for 900", quantity = 3, unitPrice = 300, lineTotal = 900 },
                },
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (bill, breakdown) = await client.GetBillAsync((await response.ReadAsync<Created>())!.Id);
        Assert.Equal("Tonkatsu Ya", bill.Title);
        Assert.Equal("Mika", Assert.Single(bill.People).Name);
        var receipt = Assert.Single(bill.Receipts);
        Assert.Equal(ReceiptSource.Manual, receipt.Source);
        Assert.Equal(new PayerDoc(bill.People[0].Id, 3300), Assert.Single(receipt.Payers));
        Assert.Equal(3300, breakdown!.Receipts[0].ComputedTotal);
        Assert.True(breakdown.CanShare);
    }

    [Fact]
    public async Task Manual_bill_without_a_name_has_no_breakdown_yet()
    {
        using var client = factory.NewClient();

        var response = await client.PostAsJsonAsync("/api/bills", new { source = "manual", currency = new { code = "KWD", digits = 3 } });

        var (bill, breakdown) = await client.GetBillAsync((await response.ReadAsync<Created>())!.Id);
        Assert.Empty(bill.People);
        Assert.Empty(bill.Receipts);
        Assert.Equal("", bill.Title);
        Assert.Null(breakdown);
    }

    public static TheoryData<string> BadBodies() => new()
    {
        """{}""",
        """{ "source": "scan" }""",
        """{ "source": 1 }""",
        """{ "source": "manual" }""",
        """{ "source": "manual", "currency": { "code": "php", "digits": 2 } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 1 } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "organizerName": "   " }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "organizerName": "A name that is far too long to be anyone's name" }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "draft": { "label": "x", "confirmedTotal": 0, "lines": [ { "name": "x", "quantity": 0, "unitPrice": 1 } ] } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "draft": { "label": "x", "confirmedTotal": 0, "lines": [ { "name": "x", "quantity": 1, "unitPrice": 100000001 } ] } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "draft": { "label": "x", "confirmedTotal": 0, "lines": [ { "name": "x", "quantity": 101, "unitPrice": 1 } ] } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "draft": { "label": "", "confirmedTotal": 0, "lines": [] } }""",
        """{ "source": "manual", "currency": { "code": "PHP", "digits": 2 }, "draft": { "label": "x", "confirmedTotal": 0, "lines": [], "adjustments": [ { "kind": "tip", "label": "Tip", "amount": 5, "rate": 10001 } ] } }""",
        """not json""",
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task Bad_requests_are_400_and_create_nothing(string body)
    {
        using var client = factory.NewClient();

        var response = await client.PostAsync("/api/bills", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("organizerKey", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creating_too_many_bills_from_one_IP_gets_429()
    {
        using var limited = new ApiFactory().With("RateLimits:CreatePerHour", "2");
        using var client = limited.NewClient();
        using var other = limited.NewClient();

        await client.CreateSampleAsync();
        await client.CreateSampleAsync();
        var third = await client.PostAsJsonAsync("/api/bills", new { source = "sample" });

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("\"rate\"", await third.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/bills", new { source = "sample" })).StatusCode);
    }
}
