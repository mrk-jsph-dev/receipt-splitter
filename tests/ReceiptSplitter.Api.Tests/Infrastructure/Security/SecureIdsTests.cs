using ReceiptSplitter.Api.Infrastructure.Security;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Security;

public class SecureIdsTests
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Secrets_are_22_base62_characters_and_never_repeat()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 10_000; i++)
        {
            var id = SecureIds.NewSecret();
            Assert.Equal(22, id.Length);
            Assert.All(id, c => Assert.Contains(c, Base62));
            Assert.True(SecureIds.IsSecret(id));
            Assert.True(seen.Add(id));
        }
    }

    [Fact]
    public void Secrets_use_the_whole_alphabet_in_every_position_but_the_first()
    {
        // 128 bits fill 21.5 base62 digits, so the first character only reaches the low digits.
        var ids = Enumerable.Range(0, 10_000).Select(_ => SecureIds.NewSecret()).ToList();
        Assert.True(ids.Select(id => id[0]).Distinct().Count() < 62);
        for (var position = 1; position < 22; position++)
            Assert.Equal(62, ids.Select(id => id[position]).Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaa-a")]
    [InlineData("../../aaaaaaaaaaaaaaaa")]
    public void Malformed_secrets_are_refused(string? value) => Assert.False(SecureIds.IsSecret(value));

    [Fact]
    public void Organizer_key_matches_only_its_own_hash()
    {
        var key = SecureIds.NewSecret();
        var other = SecureIds.NewSecret();
        var hash = OrganizerKey.Hash(key);

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(key, hash);
        Assert.True(OrganizerKey.Matches(key, hash));
        Assert.False(OrganizerKey.Matches(other, hash));
        Assert.False(OrganizerKey.Matches(null, hash));
        Assert.False(OrganizerKey.Matches("", hash));
        Assert.False(OrganizerKey.Matches(hash, hash));
    }
}
