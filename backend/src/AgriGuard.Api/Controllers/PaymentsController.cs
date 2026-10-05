using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AgriGuard.Application.Payments;
using AgriGuard.Domain.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// The payment provider's side of card payments: its signed webhook, and the two small pages the
/// farmer's browser lands on after the checkout page. All anonymous, because Stripe and the
/// browser carry no AgriGuard token; none of them can mark an order paid on its own say-so.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed partial class PaymentsController(IPaymentService payments, ILogger<PaymentsController> logger) : ControllerBase
{
    /// <summary>
    /// Stripe's webhook. The body is read raw, because the signature covers the exact bytes sent;
    /// 400 when the signature does not prove Stripe sent it. Configure the endpoint in Stripe with
    /// the events checkout.session.completed, checkout.session.async_payment_succeeded and
    /// checkout.session.expired.
    /// </summary>
    [HttpPost("api/payments/stripe/webhook")]
    [RequestSizeLimit(256 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);

        try
        {
            await payments.HandleWebhookAsync(payload, Request.Headers["Stripe-Signature"].FirstOrDefault(), ct);
        }
        catch (WebhookSignatureException ex)
        {
            logger.LogWarning("Rejected a Stripe webhook: {Reason}", ex.Message);
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid webhook signature", detail: ex.Message);
        }

        return Ok(new { received = true });
    }

    /// <summary>Where the browser lands after paying. It re-reads the session from Stripe and says how it went.</summary>
    [HttpGet("payments/return")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ContentResult> Return([FromQuery(Name = "session_id")] string? sessionId, CancellationToken ct)
    {
        if (sessionId is null || !SessionId().IsMatch(sessionId))
            return Page("Payment not found", "We could not find that payment. Open the AgriGuard app and look at the order.", success: false);

        var result = await payments.CompleteFromReturnAsync(sessionId, ct);
        if (!result.Found)
            return Page("Payment not found", "We could not find that payment. Open the AgriGuard app and look at the order.", success: false);

        var order = HtmlEncoder.Default.Encode(result.OrderNo ?? "your order");
        return result switch
        {
            { Paid: true, Status: PaymentAttemptStatus.Succeeded } =>
                Page("Payment received", $"Thank you. Order {order} is paid. Go back to the AgriGuard app to see it, and show your pickup code at the shop.", success: true),
            { Paid: true } =>
                Page("Order already paid", $"Order {order} had already been paid, so this card payment will be refunded. Go back to the AgriGuard app.", success: false),
            _ =>
                Page("Payment not finished", $"Order {order} is not paid yet. Go back to the AgriGuard app; it will check again.", success: false)
        };
    }

    /// <summary>Where the browser lands when the farmer leaves the checkout page without paying.</summary>
    [HttpGet("payments/cancelled")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ContentResult Cancelled() =>
        Page("Payment cancelled", "Nothing was charged. Go back to the AgriGuard app to pay by card again, or pay in cash when you collect the order.", success: false);

    /// <summary>A small self-contained page: no scripts, no outside requests, readable on a phone.</summary>
    private static ContentResult Page(string title, string message, bool success)
    {
        var accent = success ? "#276749" : "#8b6340";
        var mark = success ? "&#10003;" : "&#8505;";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex">
              <title>{{HtmlEncoder.Default.Encode(title)}} · AgriGuard</title>
              <style>
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f6f4ee; color: #1c1917;
                       font: 16px/1.5 system-ui, "Segoe UI", Roboto, sans-serif; padding: 16px; box-sizing: border-box; }
                main { max-width: 420px; background: #fff; border: 1px solid #e7e3d8; border-radius: 16px; padding: 32px 24px; text-align: center; }
                .mark { width: 56px; height: 56px; border-radius: 50%; margin: 0 auto 16px; display: grid; place-items: center;
                        background: {{accent}}; color: #fff; font-size: 28px; }
                h1 { font: 600 24px/1.2 Georgia, serif; margin: 0 0 8px; color: #133025; }
                p { margin: 0; color: #44403c; }
                .brand { margin-top: 24px; font-size: 13px; color: #78716c; }
              </style>
            </head>
            <body>
              <main>
                <div class="mark" aria-hidden="true">{{mark}}</div>
                <h1>{{HtmlEncoder.Default.Encode(title)}}</h1>
                <p>{{message}}</p>
                <p class="brand">AgriGuard · Crop advice, checked for safety</p>
              </main>
            </body>
            </html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>Stripe checkout session ids: cs_test_… or cs_live_…, letters, digits and underscores.</summary>
    [GeneratedRegex("^cs_[A-Za-z0-9_]{8,250}$")]
    private static partial Regex SessionId();
}
