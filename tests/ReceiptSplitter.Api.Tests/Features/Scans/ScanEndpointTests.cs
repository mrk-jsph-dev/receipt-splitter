using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ReceiptSplitter.Api.Features.Scans;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Infrastructure.Scanning;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Features.Scans;

/// <summary>The real app with a fake receipt reader that remembers what it was sent.</summary>
public sealed class ScanFactory : ApiFactory
{
    public RecordingReader Reader { get; } = new();

    public ScanFactory() => With("Scans:DailyCap", "5").With("Scans:PerIpPerHour", "100");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IReceiptReader>(Reader));
    }
}

public sealed class RecordingReader : IReceiptReader
{
    public List<byte[]> Received { get; } = [];

    public Task<ScannedReceipt> ReadAsync(BinaryData file, CancellationToken ct)
    {
        Received.Add(file.ToArray());
        return new SampleReceiptReader().ReadAsync(file, ct);
    }
}

public class ScanEndpointTests(ScanFactory factory) : IClassFixture<ScanFactory>
{
    // A JPEG header followed by a marker that must never show up in storage or logs.
    private static byte[] Photo(string marker) => [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.ASCII.GetBytes(marker)];

    private static MultipartFormDataContent Form(byte[] file, string? previous = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "file", "receipt.jpg");
        form.Add(new StringContent("PHP"), "currencyCode");
        form.Add(new StringContent("2"), "currencyDigits");
        if (previous is not null)
            form.Add(new StringContent(previous), "previous");
        return form;
    }

    [Fact]
    public async Task A_photo_becomes_a_draft_and_its_bytes_never_reach_storage_or_logs()
    {
        using var client = factory.NewClient();
        const string marker = "SECRET-RECEIPT-PIXELS-7f3a";

        var response = await client.PostAsync("/api/scans", Form(Photo(marker)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = (await response.ReadAsync<ScanResponse>())!;
        Assert.Equal(9, scan.Draft.Lines.Count);
        Assert.Equal(525580, scan.Draft.ConfirmedTotal);
        Assert.Contains(factory.Reader.Received, bytes => Encoding.ASCII.GetString(bytes).Contains(marker));

        // Nothing was stored as a bill, and no log line or stored value holds the image.
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(marker) || line.Contains("FFD8"));
        var memory = (InMemoryBillStore)factory.Store;
        Assert.DoesNotContain(memory.Snapshot(), json => json.Contains(marker));
    }

    [Fact]
    public async Task A_second_photo_is_appended_to_the_first()
    {
        using var client = factory.NewClient();
        var first = (await (await client.PostAsync("/api/scans", Form(Photo("page-1")))).ReadAsync<ScanResponse>())!;

        var previous = System.Text.Json.JsonSerializer.Serialize(first.Draft, ApiJson.Options);
        var both = (await (await client.PostAsync("/api/scans", Form(Photo("page-2"), previous))).ReadAsync<ScanResponse>())!;

        Assert.Equal(18, both.Draft.Lines.Count);
    }

    [Fact]
    public async Task Files_that_are_not_images_or_PDFs_are_refused_before_reading()
    {
        using var client = factory.NewClient();
        var before = factory.Reader.Received.Count;

        var response = await client.PostAsync("/api/scans", Form(Encoding.ASCII.GetBytes("MZ this is not a photo")));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, factory.Reader.Received.Count);
    }

    [Fact]
    public async Task A_scanned_bill_keeps_its_fees_flagged_until_the_organizer_saves_the_receipt()
    {
        using var client = factory.NewClient();
        var scan = (await (await client.PostAsync("/api/scans", Form(Photo("bill")))).ReadAsync<ScanResponse>())!;

        var created = (await (await client.PostAsJsonAsync("/api/bills", new { source = "scan", organizerName = "Ana", currency = new { code = "PHP", digits = 2 }, draft = scan.Draft }, ApiJson.Options)).ReadAsync<Created>())!;
        var bill = await client.GetBillAsync(created.Id);

        Assert.Equal(ReceiptSource.Scan, bill.Bill.Receipts[0].Source);
        Assert.All(bill.Bill.Receipts[0].Adjustments, a => Assert.Equal(AdjustmentSource.Scan, a.Source));
        Assert.False(bill.Breakdown!.Receipts[0].Matches);
        Assert.Equal(-9000, bill.Breakdown.Receipts[0].Difference);
    }
}

/// <summary>Its own app, so the daily count starts from zero.</summary>
public class ScanCapTests
{
    [Fact]
    public async Task At_the_daily_cap_scanning_stops_and_the_sample_still_works()
    {
        await using var factory = new ScanFactory();
        factory.With("Scans:DailyCap", "2");
        using var client = factory.NewClient();
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        MultipartFormDataContent Form()
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(photo), "file", "r.jpg" } };
            return form;
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/scans", Form())).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<ScanQuota>("/api/scans/quota", ApiJson.Options))!.RemainingToday);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/scans", Form())).StatusCode);
        var capped = await client.PostAsync("/api/scans", Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Contains("daily-cap", await capped.Content.ReadAsStringAsync());
        Assert.Equal(0, (await client.GetFromJsonAsync<ScanQuota>("/api/scans/quota", ApiJson.Options))!.RemainingToday);
        Assert.NotNull(await client.CreateSampleAsync());
    }

    [Fact]
    public async Task Each_network_gets_a_few_scans_an_hour()
    {
        await using var factory = new ScanFactory();
        factory.With("Scans:PerIpPerHour", "1").With("Scans:DailyCap", "100");
        using var client = factory.NewClient("10.1.2.3");
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1 };

        var first = await client.PostAsync("/api/scans", new MultipartFormDataContent { { new ByteArrayContent(photo), "file", "r.jpg" } });
        var second = await client.PostAsync("/api/scans", new MultipartFormDataContent { { new ByteArrayContent(photo), "file", "r.jpg" } });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
