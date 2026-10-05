using System.Text.Json;
using AgriGuard.Application.Payments;
using AgriGuard.Infrastructure.Payments;

namespace AgriGuard.UnitTests.Inventory;

/// <summary>The two places AgriGuard reads what Stripe sends: a webhook's signature, and a checkout session.</summary>
public sealed class StripeTests
{
    private const string Secret = "whsec_unit_test_secret";
    private const string Payload = """{"id":"evt_1","type":"checkout.session.completed","data":{"object":{"object":"checkout.session","id":"cs_test_abc"}}}""";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact]
    public void A_webhook_signed_with_the_secret_is_accepted()
    {
        var header = StripeWebhookSignature.Header(Payload, Secret, Now.ToUnixTimeSeconds());

        StripeWebhookSignature.Verify(Payload, header, Secret, Now, Tolerance);
    }

    [Fact]
    public void A_changed_body_is_rejected_even_with_a_real_signature()
    {
        var header = StripeWebhookSignature.Header(Payload, Secret, Now.ToUnixTimeSeconds());

        Assert.Throws<WebhookSignatureException>(() =>
            StripeWebhookSignature.Verify(Payload.Replace("cs_test_abc", "cs_test_other"), header, Secret, Now, Tolerance));
    }

    [Fact]
    public void A_signature_made_with_another_secret_is_rejected()
    {
        var header = StripeWebhookSignature.Header(Payload, "whsec_someone_else", Now.ToUnixTimeSeconds());

        Assert.Throws<WebhookSignatureException>(() => StripeWebhookSignature.Verify(Payload, header, Secret, Now, Tolerance));
    }

    [Fact]
    public void An_old_webhook_is_rejected_as_a_possible_replay()
    {
        var header = StripeWebhookSignature.Header(Payload, Secret, Now.AddMinutes(-6).ToUnixTimeSeconds());

        var ex = Assert.Throws<WebhookSignatureException>(() => StripeWebhookSignature.Verify(Payload, header, Secret, Now, Tolerance));
        Assert.Contains("replay", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=abc,v1=00")]
    [InlineData("v1=deadbeef")]
    public void A_missing_or_malformed_header_is_rejected(string? header) =>
        Assert.Throws<WebhookSignatureException>(() => StripeWebhookSignature.Verify(Payload, header, Secret, Now, Tolerance));

    [Fact]
    public void Any_one_of_several_signatures_may_match_while_a_secret_is_rolled()
    {
        var real = StripeWebhookSignature.Header(Payload, Secret, Now.ToUnixTimeSeconds());
        var header = $"{real},v1=0000000000000000000000000000000000000000000000000000000000000000";

        StripeWebhookSignature.Verify(Payload, header, Secret, Now, Tolerance);
    }

    [Fact]
    public void A_paid_session_gives_its_amount_and_the_card_s_brand_and_last_four()
    {
        // The shape Stripe returns for GET /v1/checkout/sessions/{id}?expand[]=payment_intent.latest_charge.
        using var json = JsonDocument.Parse("""
            {
              "id": "cs_test_a1", "object": "checkout.session", "url": null,
              "status": "complete", "payment_status": "paid",
              "amount_total": 425050, "currency": "lkr", "expires_at": 1791100000,
              "payment_intent": {
                "id": "pi_123", "object": "payment_intent",
                "latest_charge": { "id": "ch_1", "payment_method_details": { "type": "card", "card": { "brand": "visa", "last4": "4242" } } }
              }
            }
            """);

        var session = StripePaymentGateway.ReadSession(json.RootElement);

        Assert.Equal("cs_test_a1", session.Id);
        Assert.Equal(CheckoutState.Complete, session.State);
        Assert.True(session.Paid);
        Assert.Equal(425050, session.AmountTotal);
        Assert.Equal("LKR", session.Currency);
        Assert.Equal("pi_123", session.PaymentIntentId);
        Assert.Equal(("visa", "4242"), (session.CardBrand, session.CardLast4));
        Assert.Null(session.Url);
    }

    [Fact]
    public void An_open_session_is_not_paid_and_keeps_its_page()
    {
        using var json = JsonDocument.Parse("""
            { "id": "cs_test_b2", "url": "https://checkout.stripe.com/c/pay/cs_test_b2", "status": "open",
              "payment_status": "unpaid", "amount_total": 1000, "currency": "lkr", "payment_intent": null }
            """);

        var session = StripePaymentGateway.ReadSession(json.RootElement);

        Assert.Equal(CheckoutState.Open, session.State);
        Assert.False(session.Paid);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_b2", session.Url!.ToString());
        Assert.Null(session.PaymentIntentId);
    }

    [Theory]
    [InlineData("sk_live_abc", false, "live key")]
    [InlineData("pk_test_abc", false, "does not look like")]
    [InlineData("sk_live_abc", true, null)]
    [InlineData("sk_test_abc", false, null)]
    [InlineData("", false, null)]
    public void Live_keys_are_refused_unless_allowed_on_purpose(string key, bool allowLive, string? failure)
    {
        var options = new PaymentOptions { Stripe = { SecretKey = key, AllowLiveKeys = allowLive } };

        var result = new PaymentOptionsValidator().Validate(null, options);

        if (failure is null)
            Assert.True(result.Succeeded);
        else
            Assert.Contains(failure, result.FailureMessage);
    }

    [Fact]
    public void A_checkout_must_stay_open_within_stripe_s_limits()
    {
        var tooShort = new PaymentOptions { CheckoutLifetime = TimeSpan.FromMinutes(10) };

        Assert.False(new PaymentOptionsValidator().Validate(null, tooShort).Succeeded);
        Assert.True(new PaymentOptionsValidator().Validate(null, new PaymentOptions()).Succeeded);
    }
}
