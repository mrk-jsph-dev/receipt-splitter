using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ReceiptSplitter.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_returns_ok()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
