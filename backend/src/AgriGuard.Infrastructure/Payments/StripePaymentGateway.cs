using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgriGuard.Application.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Payments;

/// <summary>
/// Stripe Checkout over its REST API: AgriGuard opens a hosted checkout session, the farmer pays on
/// Stripe's page, and AgriGuard reads the session back to learn whether it was paid.
///
/// - **No card data here.** The farmer types the card on Stripe's page; the session only reports
///   the brand and last four digits.
/// - **Idempotent creation.** Each attempt sends its own Idempotency-Key, so a retry after a
///   timeout returns the same session instead of opening a second one.
/// - **A pinned API version**, so a change on Stripe's side cannot silently change these fields.
/// </summary>
public sealed class StripePaymentGateway(
    HttpClient http,
    IOptions<PaymentOptions> options,
    TimeProvider timeProvider,
    ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    /// <summary>The Stripe API version these requests and the parsing below were written against.</summary>
    public const string ApiVersion = "2024-06-20";

    private StripeOptions Stripe => options.Value.Stripe;

    public string Name => "stripe";

    public bool IsConfigured => Stripe.SecretKey.Length > 0;

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        // Stripe's form encoding: nested fields as bracketed keys.
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("payment_method_types[0]", "card"),
            new("client_reference_id", request.PaymentId.ToString()),
            new("line_items[0][quantity]", "1"),
            new("line_items[0][price_data][currency]", request.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", request.AmountMinor.ToString(CultureInfo.InvariantCulture)),
            new("line_items[0][price_data][product_data][name]", $"AgriGuard order {request.OrderNo}"),
            new("line_items[0][price_data][product_data][description]", request.Description),
            new("success_url", request.SuccessUrl),
            new("cancel_url", request.CancelUrl),
            new("expires_at", new DateTimeOffset(request.ExpiresAt, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new("metadata[payment_id]", request.PaymentId.ToString()),
            new("metadata[order_no]", request.OrderNo),
            new("payment_intent_data[metadata][payment_id]", request.PaymentId.ToString()),
            new("payment_intent_data[metadata][order_no]", request.OrderNo),
            new("payment_intent_data[description]", $"AgriGuard order {request.OrderNo}"),
        };
        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
            form.Add(new("customer_email", request.CustomerEmail));

        using var message = Request(HttpMethod.Post, "v1/checkout/sessions");
        message.Content = new FormUrlEncodedContent(form);
        message.Headers.Add("Idempotency-Key", $"agriguard-checkout-{request.PaymentId:N}");

        using var document = await SendAsync(message, ct);
        return ReadSession(document.RootElement);
    }

    public async Task<CheckoutSession> GetCheckoutAsync(string sessionId, CancellationToken ct = default)
    {
        // Expanded so the card's brand and last four come back in the same call.
        using var message = Request(HttpMethod.Get, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}?expand[]=payment_intent.latest_charge");
        using var document = await SendAsync(message, ct);
        return ReadSession(document.RootElement);
    }

    public async Task ExpireCheckoutAsync(string sessionId, CancellationToken ct = default)
    {
        using var message = Request(HttpMethod.Post, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}/expire");
        try
        {
            using var _ = await SendAsync(message, ct);
        }
        catch (PaymentGatewayException ex) when (ex.InnerException is null)
        {
            // Stripe refuses to expire a session that is already complete or expired. That is the
            // state we wanted (closed), so it is not an error; the caller re-reads the session anyway.
            logger.LogInformation("Stripe did not expire checkout {SessionId}: {Reason}", sessionId, ex.Message);
        }
    }

    public GatewayEvent? ReadWebhook(string payload, string? signatureHeader)
    {
        if (Stripe.WebhookSecret.Length == 0)
            throw new WebhookSignatureException("No webhook signing secret is configured.");

        StripeWebhookSignature.Verify(payload, signatureHeader, Stripe.WebhookSecret, timeProvider.GetUtcNow(), Stripe.WebhookTolerance);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString() ?? string.Empty;
        var data = root.GetProperty("data").GetProperty("object");
        if (Text(data, "object") != "checkout.session" || Text(data, "id") is not { } sessionId)
            return null;

        return new GatewayEvent(type, sessionId);
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        if (!IsConfigured)
            throw new PaymentGatewayException("Card payments are not configured: no Stripe secret key.");

        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Stripe.SecretKey);
        message.Headers.Add("Stripe-Version", ApiVersion);
        return message;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException("Stripe could not be reached.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(body);

            // Stripe explains every refusal in error.message. The key is never in it, and never logged.
            var reason = TryReadError(body) ?? response.ReasonPhrase ?? "no reason given";
            logger.LogWarning("Stripe answered {Status} to {Method} {Path}: {Reason}",
                (int)response.StatusCode, message.Method, message.RequestUri?.AbsolutePath, reason);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new PaymentGatewayException("Stripe refused the API key.", new UnauthorizedAccessException(reason));
            throw new PaymentGatewayException($"Stripe refused the request: {reason}");
        }
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) ? Text(error, "message") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the fields AgriGuard relies on from a Checkout Session object.</summary>
    public static CheckoutSession ReadSession(JsonElement s)
    {
        var state = Text(s, "status") switch
        {
            "complete" => CheckoutState.Complete,
            "expired" => CheckoutState.Expired,
            _ => CheckoutState.Open
        };

        string? intentId = null, brand = null, last4 = null;
        if (s.TryGetProperty("payment_intent", out var intent))
        {
            if (intent.ValueKind == JsonValueKind.String)
                intentId = intent.GetString();
            else if (intent.ValueKind == JsonValueKind.Object)
            {
                intentId = Text(intent, "id");
                if (intent.TryGetProperty("latest_charge", out var charge) && charge.ValueKind == JsonValueKind.Object
                    && charge.TryGetProperty("payment_method_details", out var details)
                    && details.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
                {
                    brand = Text(card, "brand");
                    last4 = Text(card, "last4");
                }
            }
        }

        return new CheckoutSession(
            Id: Text(s, "id") ?? throw new PaymentGatewayException("Stripe returned a session without an id."),
            Url: Text(s, "url") is { } url ? new Uri(url) : null,
            State: state,
            Paid: Text(s, "payment_status") == "paid",
            AmountTotal: s.TryGetProperty("amount_total", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetInt64() : null,
            Currency: Text(s, "currency")?.ToUpperInvariant(),
            PaymentIntentId: intentId,
            CardBrand: brand,
            CardLast4: last4,
            ExpiresAt: s.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(expires.GetInt64()).UtcDateTime
                : null);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
