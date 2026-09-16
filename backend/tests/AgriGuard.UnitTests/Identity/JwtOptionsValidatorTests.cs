using AgriGuard.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace AgriGuard.UnitTests.Identity;

public sealed class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    private static JwtOptions Valid() => new()
    {
        Issuer = "AgriGuard",
        Audience = "AgriGuard.Clients",
        // 32 bytes, base64-encoded — what `openssl rand -base64 32` gives you.
        SigningKey = Convert.ToBase64String(new byte[32]),
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(14)
    };

    [Fact]
    public void Accepts_a_complete_configuration()
    {
        Assert.True(_validator.Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void Rejects_a_missing_signing_key_with_the_command_to_fix_it()
    {
        var options = Valid();
        options.SigningKey = "";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("user-secrets", result.FailureMessage);
    }

    [Fact]
    public void Rejects_a_short_signing_key()
    {
        var options = Valid();
        options.SigningKey = "too-short-for-hmac-sha256";

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Accepts_a_long_plain_text_passphrase()
    {
        var options = Valid();
        options.SigningKey = new string('k', 40);

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Rejects_an_access_token_that_lives_too_long()
    {
        var options = Valid();
        options.AccessTokenLifetime = TimeSpan.FromHours(8);

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_a_refresh_token_shorter_than_the_access_token()
    {
        var options = Valid();
        options.RefreshTokenLifetime = TimeSpan.FromMinutes(5);

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_issuer_or_audience(string blank)
    {
        var missingIssuer = Valid();
        missingIssuer.Issuer = blank;
        Assert.True(_validator.Validate(null, missingIssuer).Failed);

        var missingAudience = Valid();
        missingAudience.Audience = blank;
        Assert.True(_validator.Validate(null, missingAudience).Failed);
    }

    [Fact]
    public void Signing_key_decodes_to_at_least_32_bytes()
    {
        Assert.True(Valid().SigningKeyBytes.Length >= 32);
    }
}
