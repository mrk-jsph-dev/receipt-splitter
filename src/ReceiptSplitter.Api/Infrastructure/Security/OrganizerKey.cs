using System.Security.Cryptography;
using System.Text;

namespace ReceiptSplitter.Api.Infrastructure.Security;

/// <summary>
/// The organizer key is sent once on create and stored only as a SHA-256 hash. It travels in the
/// X-Organizer-Key header, never in a query string, and is compared in constant time.
/// </summary>
public static class OrganizerKey
{
    public const string Header = "X-Organizer-Key";

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool Matches(string? key, string storedHash)
    {
        // Hash whatever arrived so a wrong-shape key takes the same path as a wrong key.
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(key ?? ""));
        var expected = Convert.FromHexString(storedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && SecureIds.IsSecret(key);
    }
}
