using System.Numerics;
using System.Security.Cryptography;

namespace ReceiptSplitter.Api.Infrastructure.Security;

/// <summary>Random ids from the crypto RNG. Never a counter, a date or a name.</summary>
public static class SecureIds
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>62^22 is just above 2^128, so 22 characters hold any 128-bit value.</summary>
    public const int SecretLength = 22;

    private const int ShortLength = 8;

    /// <summary>128 random bits as 22 base62 characters. Used for bill ids and organizer keys.</summary>
    public static string NewSecret()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

        Span<char> chars = stackalloc char[SecretLength];
        for (var i = SecretLength - 1; i >= 0; i--)
        {
            value = BigInteger.DivRem(value, Alphabet.Length, out var digit);
            chars[i] = Alphabet[(int)digit];
        }
        return new string(chars);
    }

    /// <summary>A short id for people, receipts, lines and activity. Unique within one bill.</summary>
    public static string NewShort() => RandomNumberGenerator.GetString(Alphabet, ShortLength);

    /// <summary>Checks the shape only, so malformed ids are turned away before any storage call.</summary>
    public static bool IsSecret(string? value) =>
        value is { Length: SecretLength } && value.AsSpan().IndexOfAnyExcept(Alphabet) < 0;
}
