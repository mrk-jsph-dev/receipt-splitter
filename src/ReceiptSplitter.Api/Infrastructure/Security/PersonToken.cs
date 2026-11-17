namespace ReceiptSplitter.Api.Infrastructure.Security;

/// <summary>
/// Proof that a phone is a given person in a bill. A 128-bit secret handed out once, in this
/// response header, when the phone joins or picks a name; stored only as a SHA-256 hash on the
/// person. The phone sends it back in the same header. Without it, a link holder can't change
/// anyone's claims, payment, name or card.
/// </summary>
public static class PersonToken
{
    public const string Header = "X-Person-Token";

    /// <summary>A new token and the hash to store. Hashed like the organizer key.</summary>
    public static (string Token, string Hash) New()
    {
        var token = SecureIds.NewSecret();
        return (token, OrganizerKey.Hash(token));
    }
}
