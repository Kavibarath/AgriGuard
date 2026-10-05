using System.Collections.Concurrent;
using System.Text.Json;
using AgriGuard.Application.Payments;
using AgriGuard.Infrastructure.Payments;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Stripe, so the tests never reach the internet. Sessions live in memory; a test
/// "pays" one the way a farmer would on Stripe's page. Webhooks go through the real signature check.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string WebhookSecret = "whsec_test_only_secret";

    private readonly ConcurrentDictionary<string, CheckoutSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, string> _byAttempt = new();

    public string Name => "stripe";

    public bool IsConfigured { get; set; } = true;

    /// <summary>When true, every call fails as if Stripe could not be reached.</summary>
    public bool Down { get; set; }

    public ConcurrentQueue<CheckoutRequest> Requests { get; } = new();

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        ThrowIfDown();
        Requests.Enqueue(request);

        // Like Stripe's Idempotency-Key: the same attempt gets the same session.
        var id = _byAttempt.GetOrAdd(request.PaymentId, _ => $"cs_test_{Guid.NewGuid():N}");
        var session = _sessions.GetOrAdd(id, _ => new CheckoutSession(
            id, new Uri($"https://checkout.stripe.test/c/pay/{id}"), CheckoutState.Open, false,
            request.AmountMinor, request.Currency.ToUpperInvariant(), null, null, null, request.ExpiresAt));
        return Task.FromResult(session);
    }

    public Task<CheckoutSession> GetCheckoutAsync(string sessionId, CancellationToken ct = default)
    {
        ThrowIfDown();
        return _sessions.TryGetValue(sessionId, out var session)
            ? Task.FromResult(session)
            : throw new PaymentGatewayException($"No such checkout session: {sessionId}");
    }

    public Task ExpireCheckoutAsync(string sessionId, CancellationToken ct = default)
    {
        ThrowIfDown();
        _sessions.AddOrUpdate(sessionId, _ => throw new PaymentGatewayException("No such session"),
            (_, s) => s.State == CheckoutState.Open ? s with { State = CheckoutState.Expired, Url = null } : s);
        return Task.CompletedTask;
    }

    public GatewayEvent? ReadWebhook(string payload, string? signatureHeader)
    {
        StripeWebhookSignature.Verify(payload, signatureHeader, WebhookSecret, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data").GetProperty("object");
        return data.GetProperty("object").GetString() == "checkout.session"
            ? new GatewayEvent(document.RootElement.GetProperty("type").GetString()!, data.GetProperty("id").GetString()!)
            : null;
    }

    /// <summary>What a farmer does on Stripe's page: pays with a card. <paramref name="amountMinor"/> overrides the amount taken.</summary>
    public void Pay(string sessionId, string brand = "visa", string last4 = "4242", long? amountMinor = null) =>
        _sessions.AddOrUpdate(sessionId, _ => throw new InvalidOperationException("No such session"), (_, s) => s with
        {
            State = CheckoutState.Complete,
            Paid = true,
            Url = null,
            PaymentIntentId = $"pi_test_{Guid.NewGuid():N}",
            CardBrand = brand,
            CardLast4 = last4,
            AmountTotal = amountMinor ?? s.AmountTotal
        });

    /// <summary>The page timed out unpaid.</summary>
    public void Expire(string sessionId) =>
        _sessions.AddOrUpdate(sessionId, _ => throw new InvalidOperationException("No such session"),
            (_, s) => s with { State = CheckoutState.Expired, Url = null });

    public CheckoutState StateOf(string sessionId) => _sessions[sessionId].State;

    /// <summary>A webhook body and the header Stripe would sign it with.</summary>
    public static (string Payload, string Signature) Webhook(string type, string sessionId, string secret = WebhookSecret)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type,
            data = new { @object = new { @object = "checkout.session", id = sessionId } }
        });
        return (payload, StripeWebhookSignature.Header(payload, secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    private void ThrowIfDown()
    {
        if (Down)
            throw new PaymentGatewayException("Stripe could not be reached.", new HttpRequestException("down"));
    }
}
