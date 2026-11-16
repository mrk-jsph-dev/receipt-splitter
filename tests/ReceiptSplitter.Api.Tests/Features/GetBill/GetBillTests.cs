using System.Net;

namespace ReceiptSplitter.Api.Tests.Features.GetBill;

public class GetBillTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Answers_304_while_nothing_changed_and_200_with_a_new_ETag_after_a_change()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        var first = await client.GetAsync($"/api/bills/{created.Id}");
        var etag = first.Headers.ETag!;
        Assert.Equal("\"1\"", etag.Tag);

        var unchanged = await Conditional(client, created.Id, etag.Tag);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal(etag, unchanged.Headers.ETag);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        await client.PatchAsync(created.Id, new { op = "join", name = "Zed" });

        var changed = await Conditional(client, created.Id, etag.Tag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("\"2\"", changed.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Every_response_carries_the_privacy_headers()
    {
        using var client = factory.NewClient();
        var created = await client.CreateSampleAsync();

        foreach (var response in new[]
        {
            await client.GetAsync($"/api/bills/{created.Id}"),
            await client.GetAsync("/api/bills/nope"),
            await client.GetAsync("/healthz"),
        })
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
            Assert.Equal("noindex", Single(response, "X-Robots-Tag"));
            Assert.Equal("default-src 'none'; frame-ancestors 'none'", Single(response, "Content-Security-Policy"));
        }
    }

    [Fact]
    public async Task CORS_allows_only_the_app_origin_and_exposes_the_ETag()
    {
        using var withOrigin = new ApiFactory().With("Cors:AllowedOrigins:0", "https://app.example");
        using var client = withOrigin.NewClient();

        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/bills/abc");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "PATCH");
            return await client.SendAsync(request);
        }

        var allowed = await Preflight("https://app.example");
        var other = await Preflight("https://evil.example");

        Assert.Equal("https://app.example", Single(allowed, "Access-Control-Allow-Origin"));
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));

        var get = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        get.Headers.Add("Origin", "https://app.example");
        Assert.Contains("ETag", Single(await client.SendAsync(get), "Access-Control-Expose-Headers"));
    }

    private static Task<HttpResponseMessage> Conditional(HttpClient client, string id, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/bills/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Reading_too_often_from_one_IP_gets_429_and_other_IPs_still_read()
    {
        using var limited = new ApiFactory().With("RateLimits:ReadsPerMinute", "2");
        using var client = limited.NewClient();
        using var other = limited.NewClient();
        var created = await client.CreateSampleAsync();

        await client.GetBillAsync(created.Id);
        await client.GetBillAsync(created.Id);
        var third = await client.GetAsync($"/api/bills/{created.Id}");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("\"rate\"", await third.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/api/bills/{created.Id}")).StatusCode);
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.TryGetValues(header, out var values) ? values : response.Content.Headers.GetValues(header));
}
