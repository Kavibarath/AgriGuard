using System.Security.Cryptography;
using AgriGuard.Application.Auth;

namespace AgriGuard.Infrastructure.Identity;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing (§5 Security; the plan's stated alternative to BCrypt).
///
/// Stored format — everything needed to re-verify travels with the hash, so the work factor
/// can be raised later without invalidating existing passwords:
///     pbkdf2-sha256$&lt;iterations&gt;$&lt;salt-base64&gt;$&lt;hash-base64&gt;
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // OWASP's 2023 floor for PBKDF2-HMAC-SHA256. Costs ~50 ms per login on this hardware,
    // which is the point: it throttles offline brute-force if the database ever leaks.
    private const int Iterations = 600_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            return false;

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            // A corrupt or hand-edited hash must fail closed, not crash the login endpoint.
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Constant-time: a byte-by-byte early exit would leak how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
