using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Payments;

/// <summary>
/// Bound from the "Payments" section. The Stripe keys come from user-secrets locally and environment
/// variables in the cloud (Payments__Stripe__SecretKey), never from a committed file. With no secret
/// key, card payments are simply switched off and cash at the counter still works.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>ISO 4217. Prices in the catalogue are in Sri Lankan rupees.</summary>
    public string Currency { get; set; } = "LKR";

    /// <summary>
    /// Where the provider sends the farmer's browser back to after paying: this API's public
    /// address (the emulator reaches the PC's port 5000 as 10.0.2.2).
    /// </summary>
    public Uri PublicBaseUrl { get; set; } = new("http://10.0.2.2:5000");

    /// <summary>How long a checkout page stays open. Stripe allows 30 minutes to 24 hours.</summary>
    public TimeSpan CheckoutLifetime { get; set; } = TimeSpan.FromMinutes(30);

    public StripeOptions Stripe { get; set; } = new();
}

public sealed class StripeOptions
{
    public Uri ApiBaseUrl { get; set; } = new("https://api.stripe.com/");

    /// <summary>The secret API key (sk_test_…). Empty switches card payments off.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The webhook endpoint's signing secret (whsec_…). Empty switches the webhook off; the return page and the app still confirm payments.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Live keys move real money. This is an academic project, so they are refused unless this is
    /// set on purpose.
    /// </summary>
    public bool AllowLiveKeys { get; set; }

    /// <summary>How old a webhook may be and still be accepted (replay protection).</summary>
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        List<string> failures = [];

        if (options.Currency is not { Length: 3 } || !options.Currency.All(char.IsAsciiLetter))
            failures.Add("Payments:Currency must be a three-letter ISO 4217 code, such as LKR.");

        if (!options.PublicBaseUrl.IsAbsoluteUri)
            failures.Add("Payments:PublicBaseUrl must be an absolute URL.");

        if (options.CheckoutLifetime < TimeSpan.FromMinutes(30) || options.CheckoutLifetime > TimeSpan.FromHours(24))
            failures.Add("Payments:CheckoutLifetime must be between 30 minutes and 24 hours (Stripe's limits).");

        var key = options.Stripe.SecretKey;
        if (key.Length > 0)
        {
            var isTest = key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal);
            var isLive = key.StartsWith("sk_live_", StringComparison.Ordinal) || key.StartsWith("rk_live_", StringComparison.Ordinal);
            if (!isTest && !isLive)
                failures.Add("Payments:Stripe:SecretKey does not look like a Stripe secret key (sk_test_…).");
            else if (isLive && !options.Stripe.AllowLiveKeys)
                failures.Add("Payments:Stripe:SecretKey is a live key, which moves real money. Use a test key (sk_test_…).");
        }

        var webhook = options.Stripe.WebhookSecret;
        if (webhook.Length > 0 && !webhook.StartsWith("whsec_", StringComparison.Ordinal))
            failures.Add("Payments:Stripe:WebhookSecret must be the endpoint's signing secret (whsec_…).");

        if (options.Stripe.WebhookTolerance <= TimeSpan.Zero)
            failures.Add("Payments:Stripe:WebhookTolerance must be positive.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
