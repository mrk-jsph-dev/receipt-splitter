using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReceiptSplitter.Api.Tests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Healthz_returns_ok()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_reports_the_running_version()
    {
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/healthz");

        Assert.Equal("0.0.0-dev", body.GetProperty("version").GetString());
    }
}
