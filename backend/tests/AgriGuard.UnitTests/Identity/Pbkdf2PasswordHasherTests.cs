using AgriGuard.Infrastructure.Identity;

namespace AgriGuard.UnitTests.Identity;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verifies_a_password_it_hashed()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Rejects_a_wrong_password()
    {
        var hash = _hasher.Hash("AgriGuard!Demo1");

        Assert.False(_hasher.Verify("agriguard!demo1", hash));
        Assert.False(_hasher.Verify("", hash));
    }

    [Fact]
    public void Same_password_hashes_differently_each_time()
    {
        // Distinct salts: two users with the same password must not share a hash,
        // or one cracked hash cracks both.
        Assert.NotEqual(_hasher.Hash("same-password"), _hasher.Hash("same-password"));
    }

    [Fact]
    public void Hash_embeds_algorithm_iterations_and_salt()
    {
        var parts = _hasher.Hash("whatever").Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 600_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$600000$not-base64$also-not-base64")]
    [InlineData("pbkdf2-sha256$abc$c2FsdA==$aGFzaA==")]
    [InlineData("bcrypt$600000$c2FsdA==$aGFzaA==")]
    public void Malformed_stored_hash_fails_instead_of_throwing(string storedHash)
    {
        Assert.False(_hasher.Verify("any password", storedHash));
    }

    [Fact]
    public void Hash_fits_the_database_column()
    {
        // users.password_hash is varchar(256).
        Assert.True(_hasher.Hash(new string('x', 200)).Length <= 256);
    }
}
