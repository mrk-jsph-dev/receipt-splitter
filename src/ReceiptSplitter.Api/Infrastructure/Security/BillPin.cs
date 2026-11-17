using System.Security.Cryptography;
using System.Text;

namespace ReceiptSplitter.Api.Infrastructure.Security;

/// <summary>
/// The optional bill PIN. Stored only as a salted PBKDF2-SHA256 hash. A friend types it once and
/// gets a token, an HMAC of the bill id keyed by that hash, which the phone sends in the X-Bill-Pin
/// header from then on. The slow hash runs only when someone types the PIN, and a new or removed
/// PIN ends every token, because the hash it was keyed by is gone.
/// </summary>
public static class BillPin
{
    public const string Header = "X-Bill-Pin";

    private const string Scheme = "pbkdf2-sha256";
    // OWASP's 2023 guidance for PBKDF2-HMAC-SHA256.
    private const int Iterations = 600_000;

    /// <summary>4 to 6 ASCII digits. Other scripts' digits are refused so a keypad always types it.</summary>
    public static bool IsValid(string? pin) => pin is { Length: >= 4 and <= 6 } && pin.All(char.IsAsciiDigit);

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Matches(string? pin, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
            return false;
        var expected = Convert.FromBase64String(parts[3]);
        // A badly shaped PIN still takes the slow path, so timing says nothing about why it failed.
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin ?? "", Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && IsValid(pin);
    }

    public static string Token(string stored, string billId) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(billId)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TokenMatches(string? token, string stored, string billId) =>
        token is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(Token(stored, billId)));
}
