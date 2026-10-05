using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgriGuard.Application.Payments;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Paying for an order: by card through the (fake) provider's checkout, or in cash at the counter.
/// The thread through every test: an order is paid only when the provider itself says so, every
/// path is safe to repeat, and nothing is handed over unpaid.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentTests(AgriGuardApiFactory factory)
{
    private FakePaymentGateway Stripe => factory.Payments;

    /// <summary>An approved prescription's order, with its dealer and its farmer signed in.</summary>
    private async Task<(HttpClient Dealer, HttpClient Farmer, Guid OrderId)> OrderAsync()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        (await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var owner = await factory.QueryAsync(db => db.Users.SingleAsync(u => u.Id == run.Dealer.UserId));
        var orderId = await factory.QueryAsync(db => db.InputOrders.Where(o => o.Prescription!.AgentRunId == run.RunId).Select(o => o.Id).SingleAsync());
        return (await factory.SignedInAsAsync(owner), run.Farmer, orderId);
    }

    private static async Task<(Guid PaymentId, string Url)> StartCardAsync(HttpClient farmer, Guid orderId)
    {
        var response = await farmer.PostAsync($"/api/orders/mine/{orderId}/payments/card", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("paymentId").GetGuid(), body.GetProperty("checkoutUrl").GetString()!);
    }

    private static async Task<JsonElement> SyncAsync(HttpClient farmer, Guid orderId, Guid paymentId)
    {
        var response = await farmer.PostAsync($"/api/orders/mine/{orderId}/payments/{paymentId}/sync", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<string> SessionOfAsync(Guid paymentId) =>
        factory.QueryAsync(db => db.Payments.Where(p => p.Id == paymentId).Select(p => p.ProviderReference!).SingleAsync());

    private Task<InputOrder> OrderRowAsync(Guid orderId) =>
        factory.QueryAsync(db => db.InputOrders.AsNoTracking().Include(o => o.Payments).SingleAsync(o => o.Id == orderId));

    private static Task<HttpResponseMessage> CashAsync(HttpClient dealer, Guid orderId) =>
        dealer.PostAsync($"/api/orders/{orderId}/payments/cash", null);

    // ── Card ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_card_payment_marks_the_order_paid_only_once_the_provider_says_it_is()
    {
        var (dealer, farmer, orderId) = await OrderAsync();
        var total = (await OrderRowAsync(orderId)).TotalAmount;

        var (paymentId, url) = await StartCardAsync(farmer, orderId);
        var beforePaying = await SyncAsync(farmer, orderId, paymentId);
        Stripe.Pay(await SessionOfAsync(paymentId));
        var afterPaying = await SyncAsync(farmer, orderId, paymentId);

        Assert.StartsWith("https://checkout.stripe.test/", url);
        Assert.Equal("Unpaid", beforePaying.GetProperty("paymentStatus").GetString());
        Assert.Equal("Pending", beforePaying.GetProperty("latestAttemptStatus").GetString());
        Assert.Equal("Paid", afterPaying.GetProperty("paymentStatus").GetString());
        Assert.Equal("Card", afterPaying.GetProperty("paidBy").GetString());
        Assert.Equal("visa", afterPaying.GetProperty("cardBrand").GetString());
        Assert.Equal("4242", afterPaying.GetProperty("cardLast4").GetString());

        // The provider was asked for exactly the order's total, in cents, and told where to send the farmer back.
        var request = Stripe.Requests.Last(r => r.PaymentId == paymentId);
        Assert.Equal((long)(total * 100), request.AmountMinor);
        Assert.Equal("LKR", request.Currency);
        Assert.EndsWith("/payments/return?session_id={CHECKOUT_SESSION_ID}", request.SuccessUrl);

        // Both sides see it paid; the farmer is not offered to pay again.
        var dealerView = await dealer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Paid", dealerView.GetProperty("paymentStatus").GetString());
        Assert.Equal("Card", dealerView.GetProperty("paidBy").GetString());
        var mine = (await farmer.GetFromJsonAsync<JsonElement>("/api/orders/mine")).Items().Single(o => o.GetProperty("id").GetGuid() == orderId);
        Assert.False(mine.GetProperty("canPayByCard").GetBoolean());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("openPaymentId").ValueKind);
    }

    [Fact]
    public async Task A_second_tap_reuses_the_open_checkout_instead_of_opening_another()
    {
        var (_, farmer, orderId) = await OrderAsync();

        var first = await StartCardAsync(farmer, orderId);
        var second = await StartCardAsync(farmer, orderId);
        var mine = (await farmer.GetFromJsonAsync<JsonElement>("/api/orders/mine")).Items().Single(o => o.GetProperty("id").GetGuid() == orderId);

        Assert.Equal(first, second);
        Assert.Single(Stripe.Requests, r => r.PaymentId == first.PaymentId);
        Assert.Equal(first.PaymentId, mine.GetProperty("openPaymentId").GetGuid());
        Assert.True(mine.GetProperty("canPayByCard").GetBoolean());
    }

    [Fact]
    public async Task Only_the_farmer_the_order_belongs_to_can_pay_it_in_the_app()
    {
        var (dealer, _, orderId) = await OrderAsync();
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));
        var agronomist = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist));

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/orders/mine/{orderId}/payments/card", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agronomist.PostAsync($"/api/orders/mine/{orderId}/payments/card", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dealer.PostAsync($"/api/orders/mine/{orderId}/payments/card", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/orders/mine/{Guid.NewGuid()}/payments/card", null)).StatusCode);
    }

    [Fact]
    public async Task An_expired_checkout_is_recorded_and_paying_again_opens_a_new_one()
    {
        var (_, farmer, orderId) = await OrderAsync();
        var (expiredId, _) = await StartCardAsync(farmer, orderId);
        Stripe.Expire(await SessionOfAsync(expiredId));

        var synced = await SyncAsync(farmer, orderId, expiredId);
        var (freshId, _) = await StartCardAsync(farmer, orderId);

        Assert.Equal("Unpaid", synced.GetProperty("paymentStatus").GetString());
        Assert.Equal("Expired", synced.GetProperty("latestAttemptStatus").GetString());
        Assert.NotEqual(expiredId, freshId);
    }

    [Fact]
    public async Task Money_taken_that_is_not_the_amount_asked_for_never_marks_the_order_paid()
    {
        var (_, farmer, orderId) = await OrderAsync();
        var (paymentId, _) = await StartCardAsync(farmer, orderId);
        var request = Stripe.Requests.Last(r => r.PaymentId == paymentId);
        Stripe.Pay(await SessionOfAsync(paymentId), amountMinor: request.AmountMinor - 100);

        var synced = await SyncAsync(farmer, orderId, paymentId);
        var attempt = (await OrderRowAsync(orderId)).Payments.Single(p => p.Id == paymentId);

        Assert.Equal("Unpaid", synced.GetProperty("paymentStatus").GetString());
        Assert.Equal("Failed", synced.GetProperty("latestAttemptStatus").GetString());
        Assert.True(attempt.RefundDue);
    }

    // ── The provider's word: the return page and the webhook ─────────────────

    [Fact]
    public async Task The_page_the_browser_returns_to_settles_the_payment_from_the_provider()
    {
        var (_, farmer, orderId) = await OrderAsync();
        var (paymentId, _) = await StartCardAsync(farmer, orderId);
        var session = await SessionOfAsync(paymentId);
        Stripe.Pay(session);
        var anonymous = factory.CreateClient();

        var page = await anonymous.GetStringAsync($"/payments/return?session_id={session}");
        var unknown = await anonymous.GetStringAsync("/payments/return?session_id=cs_test_unknownsession");
        var malformed = await anonymous.GetStringAsync("/payments/return?session_id=<script>");

        Assert.Contains("Payment received", page);
        Assert.Equal(OrderPaymentStatus.Paid, (await OrderRowAsync(orderId)).PaymentStatus);
        Assert.Contains("Payment not found", unknown);
        Assert.Contains("Payment not found", malformed);
        Assert.DoesNotContain("<script>", malformed);
    }

    [Fact]
    public async Task Coming_back_without_paying_changes_nothing()
    {
        var (_, farmer, orderId) = await OrderAsync();
        var (paymentId, _) = await StartCardAsync(farmer, orderId);
        var anonymous = factory.CreateClient();

        var page = await anonymous.GetStringAsync($"/payments/return?session_id={await SessionOfAsync(paymentId)}");
        var cancelled = await anonymous.GetStringAsync("/payments/cancelled");

        Assert.Contains("Payment not finished", page);
        Assert.Contains("Nothing was charged", cancelled);
        Assert.Equal(OrderPaymentStatus.Unpaid, (await OrderRowAsync(orderId)).PaymentStatus);
    }

    [Fact]
    public async Task A_signed_webhook_settles_the_payment_and_a_forged_one_is_refused()
    {
        var (_, farmer, orderId) = await OrderAsync();
        var (paymentId, _) = await StartCardAsync(farmer, orderId);
        var session = await SessionOfAsync(paymentId);
        Stripe.Pay(session);
        var anonymous = factory.CreateClient();

        var forged = FakePaymentGateway.Webhook("checkout.session.completed", session, secret: "whsec_not_the_real_one");
        var forgedResponse = await PostWebhookAsync(anonymous, forged);
        var unpaidAfterForgery = (await OrderRowAsync(orderId)).PaymentStatus;

        var real = FakePaymentGateway.Webhook("checkout.session.completed", session);
        var realResponse = await PostWebhookAsync(anonymous, real);
        // Stripe delivers at least once: the same event again must change nothing.
        var replayResponse = await PostWebhookAsync(anonymous, real);

        Assert.Equal(HttpStatusCode.BadRequest, forgedResponse.StatusCode);
        Assert.Equal(OrderPaymentStatus.Unpaid, unpaidAfterForgery);
        Assert.Equal(HttpStatusCode.OK, realResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var order = await OrderRowAsync(orderId);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Single(order.Payments, p => p.Status == PaymentAttemptStatus.Succeeded);
    }

    [Fact]
    public async Task A_webhook_about_a_session_that_is_not_ours_is_acknowledged_and_ignored()
    {
        var response = await PostWebhookAsync(factory.CreateClient(), FakePaymentGateway.Webhook("checkout.session.completed", "cs_test_from_another_app"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, (string Payload, string Signature) webhook)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/stripe/webhook")
        {
            Content = new StringContent(webhook.Payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", webhook.Signature);
        return client.SendAsync(request);
    }

    // ── Cash, and the two together ───────────────────────────────────────────

    [Fact]
    public async Task Cash_at_the_counter_pays_the_order_and_closes_an_open_card_checkout()
    {
        var (dealer, farmer, orderId) = await OrderAsync();
        var (cardId, _) = await StartCardAsync(farmer, orderId);
        var session = await SessionOfAsync(cardId);

        var response = await CashAsync(dealer, orderId);
        var repeat = await CashAsync(dealer, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paid", order.GetProperty("paymentStatus").GetString());
        Assert.Equal("Cash", order.GetProperty("paidBy").GetString());
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        var row = await OrderRowAsync(orderId);
        Assert.Single(row.Payments, p => p.Method == PaymentMethod.Cash);
        Assert.Equal(PaymentAttemptStatus.Cancelled, row.Payments.Single(p => p.Id == cardId).Status);
        Assert.Equal(CheckoutState.Expired, Stripe.StateOf(session));
    }

    [Fact]
    public async Task A_card_paid_at_the_same_moment_as_cash_is_kept_and_flagged_for_a_refund()
    {
        var (dealer, farmer, orderId) = await OrderAsync();
        var (cardId, _) = await StartCardAsync(farmer, orderId);
        // The farmer pays on Stripe's page while the dealer takes cash, before anything synced.
        Stripe.Pay(await SessionOfAsync(cardId));

        (await CashAsync(dealer, orderId)).EnsureSuccessStatusCode();

        var row = await OrderRowAsync(orderId);
        var card = row.Payments.Single(p => p.Id == cardId);
        Assert.Equal(OrderPaymentStatus.Paid, row.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Succeeded, card.Status);
        Assert.True(card.RefundDue);
        var view = await dealer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Cash", view.GetProperty("paidBy").GetString());
    }

    [Fact]
    public async Task Nothing_is_handed_over_unpaid()
    {
        var (dealer, _, orderId) = await OrderAsync();
        var code = (await OrderRowAsync(orderId)).PickupCode!;
        await dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status = "Packed" });

        var unpaid = await dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status = "Collected", pickupCode = code });
        (await CashAsync(dealer, orderId)).EnsureSuccessStatusCode();
        var paid = await dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status = "Collected", pickupCode = code });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unpaid.StatusCode);
        Assert.Equal("ORDER_NOT_PAID", await unpaid.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
    }

    [Fact]
    public async Task A_collected_order_cannot_be_paid_again_and_another_shop_cannot_take_cash_for_it()
    {
        var (dealer, farmer, orderId) = await OrderAsync();
        var other = await factory.SeedShopAsync();
        var code = (await OrderRowAsync(orderId)).PickupCode!;
        await dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status = "Packed" });
        (await CashAsync(dealer, orderId)).EnsureSuccessStatusCode();
        await dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status = "Collected", pickupCode = code });

        var card = await farmer.PostAsync($"/api/orders/mine/{orderId}/payments/card", null);
        var otherShop = await CashAsync(other.Client, orderId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, card.StatusCode);
        Assert.Equal("ORDER_NOT_PAYABLE", await card.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, otherShop.StatusCode);
    }

    // ── When card payments cannot be taken ───────────────────────────────────

    [Fact]
    public async Task With_no_provider_key_card_payments_are_switched_off_and_cash_still_works()
    {
        var (dealer, farmer, orderId) = await OrderAsync();
        Stripe.IsConfigured = false;
        try
        {
            var card = await farmer.PostAsync($"/api/orders/mine/{orderId}/payments/card", null);
            var mine = (await farmer.GetFromJsonAsync<JsonElement>("/api/orders/mine")).Items().Single(o => o.GetProperty("id").GetGuid() == orderId);
            var cash = await CashAsync(dealer, orderId);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, card.StatusCode);
            Assert.Equal("CARD_PAYMENTS_UNAVAILABLE", await card.ProblemCodeAsync());
            Assert.False(mine.GetProperty("canPayByCard").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
        }
        finally
        {
            Stripe.IsConfigured = true;
        }
    }

    [Fact]
    public async Task When_the_provider_does_not_answer_the_farmer_is_told_and_the_attempt_is_recorded_as_failed()
    {
        var (_, farmer, orderId) = await OrderAsync();
        Stripe.Down = true;
        try
        {
            var response = await farmer.PostAsync($"/api/orders/mine/{orderId}/payments/card", null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("PAYMENT_PROVIDER_UNAVAILABLE", await response.ProblemCodeAsync());
            var attempt = Assert.Single((await OrderRowAsync(orderId)).Payments);
            Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        }
        finally
        {
            Stripe.Down = false;
        }

        // Once it is back, the farmer can simply try again.
        var (paymentId, _) = await StartCardAsync(farmer, orderId);
        Assert.NotEqual(Guid.Empty, paymentId);
    }
}
