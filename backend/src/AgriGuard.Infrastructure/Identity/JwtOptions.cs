using System.Text;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Identity;

/// <summary>
/// Bound from the "Jwt" configuration section. The signing key comes from user-secrets locally
/// and an environment variable on Render — never from a committed appsettings file.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "AgriGuard";
    public string Audience { get; set; } = "AgriGuard.Clients";

    /// <summary>Base64 or plain text; either way it must decode to at least 32 bytes (HMAC-SHA256's key size).</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Short-lived on purpose: a stolen access token cannot be revoked, only outlived.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Long enough that a farmer in the field is not logged out mid-season; revocable via the database.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public byte[] SigningKeyBytes => DecodeKey(SigningKey);

    /// <summary>
    /// Accepts a base64 key (what `openssl rand -base64 32` produces) and falls back to raw UTF-8,
    /// so a hand-typed passphrase also works as long as it is long enough.
    /// </summary>
    internal static byte[] DecodeKey(string key)
    {
        Span<byte> decoded = stackalloc byte[256];
        return Convert.TryFromBase64String(key, decoded, out var written) && written >= 32
            ? decoded[..written].ToArray()
            : Encoding.UTF8.GetBytes(key);
    }
}

/// <summary>
/// Fails fast at startup rather than issuing tokens anyone can forge: a short or missing key is
/// the single most damaging misconfiguration in this system.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            failures.Add("Jwt:SigningKey is not configured. Run: dotnet user-secrets set \"Jwt:SigningKey\" \"<base64 of 32+ random bytes>\" --project backend/src/AgriGuard.Api");
        else if (JwtOptions.DecodeKey(options.SigningKey).Length < 32)
            failures.Add("Jwt:SigningKey must decode to at least 32 bytes for HMAC-SHA256.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add("Jwt:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add("Jwt:Audience is required.");

        if (options.AccessTokenLifetime <= TimeSpan.Zero || options.AccessTokenLifetime > TimeSpan.FromHours(1))
            failures.Add("Jwt:AccessTokenLifetime must be between 1 second and 1 hour.");

        if (options.RefreshTokenLifetime <= options.AccessTokenLifetime)
            failures.Add("Jwt:RefreshTokenLifetime must be longer than Jwt:AccessTokenLifetime.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
