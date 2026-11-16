using System.Net;
using System.Net.Sockets;

namespace ReceiptSplitter.Api.Infrastructure.RateLimits;

/// <summary>
/// Who counts as one client for the per-IP limits. An IPv6 connection usually gets a whole /64,
/// so counting single IPv6 addresses would let one person rotate through billions of them.
/// </summary>
public static class ClientKey
{
    public static string Of(HttpContext http) => Of(http.Connection.RemoteIpAddress);

    public static string Of(IPAddress? address)
    {
        if (address is null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        bytes.AsSpan(8).Clear();
        return $"{new IPAddress(bytes)}/64";
    }
}
