using System.Net;
using System.Net.Http.Json;
using ReceiptSplitter.Api.Infrastructure.RateLimits;

namespace ReceiptSplitter.Api.Tests.Infrastructure.RateLimits;

public class ClientKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [InlineData(null, "unknown")]
    public void One_IPv4_address_or_one_IPv6_slash_64_is_one_client(string? address, string expected) =>
        Assert.Equal(expected, ClientKey.Of(address is null ? null : IPAddress.Parse(address)));

    [Fact]
    public void Neighbouring_IPv6_networks_are_different_clients() =>
        Assert.NotEqual(
            ClientKey.Of(IPAddress.Parse("2001:db8:1:2::1")),
            ClientKey.Of(IPAddress.Parse("2001:db8:1:3::1")));

    [Fact]
    public async Task Rotating_addresses_inside_one_IPv6_slash_64_shares_one_create_limit()
    {
        using var limited = new ApiFactory().With("RateLimits:CreatePerHour", "2");
        using var first = limited.NewClient("2001:db8:7:7::1");
        using var second = limited.NewClient("2001:db8:7:7::2");
        using var third = limited.NewClient("2001:db8:7:7:ffff:ffff:ffff:ffff");
        using var elsewhere = limited.NewClient("2001:db8:7:8::1");

        await first.CreateSampleAsync();
        await second.CreateSampleAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, (await third.PostAsJsonAsync("/api/bills", new { source = "sample" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await elsewhere.PostAsJsonAsync("/api/bills", new { source = "sample" })).StatusCode);
    }
}
