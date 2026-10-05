# Paying for orders: card (Stripe) and cash

Study note for Component C. An order created by an approved prescription can be paid **by card in
the farmer's phone app** (Visa or Mastercard, through Stripe Checkout in test mode), or **in cash
at the dealer's counter**. **Nothing is handed over unpaid.**

## 1. The flow

```
Phone (farmer)                    API                                   Stripe
──────────────                    ───                                   ──────
"Pay by card"  ── POST /api/orders/mine/{id}/payments/card ──►  Payment(Pending) saved
                                   ── POST v1/checkout/sessions ─────────►  session cs_…, url
               ◄── { paymentId, checkoutUrl } ──
opens checkoutUrl in a browser tab ─────────────────────────────────────►  farmer types the card
                                                                              on Stripe's page
browser lands on /payments/return?session_id=cs_… ──►  re-reads the session ◄── GET session
back in the app ── POST …/payments/{paymentId}/sync ──►  re-reads the session ◄── GET session
                                   (deployed) Stripe ── signed webhook ──►  re-reads the session
                                   session complete + paid + amount matches ⇒ order Paid
Dealer: "Record cash" ── POST /api/orders/{id}/payments/cash ──►  Payment(Cash, Succeeded), order Paid
Dealer: "Mark collected" + pickup code ──►  refused with ORDER_NOT_PAID unless Paid
```

## 2. The rules, and why

| Rule | Where | Why |
|---|---|---|
| **The card is never seen by AgriGuard.** The farmer types it on Stripe's hosted page; only the brand and last four come back. | `StripePaymentGateway` | Handling card numbers would put the whole system under PCI-DSS. Hosted checkout keeps it out of scope. |
| **An order is Paid only on Stripe's own word,** read back with our secret key (`GetCheckoutAsync`). The phone, the browser and the webhook only say *"go and check"*. | `PaymentService.ApplyAsync` | A client can be faked, and a return URL can be typed by anyone. The only trusted fact is what Stripe returns to a call we make. |
| **The amount and currency Stripe took must equal what we asked for.** Otherwise the attempt is Failed and flagged `RefundDue`. | `ApplyAsync` | Never mark an order paid on the wrong amount, whatever caused it. |
| **Amounts go in the smallest unit, exactly** (LKR 4,250.50 → 425050). An amount that does not convert exactly is refused. | `PaymentRules.ToMinorUnits` | Money must never be rounded silently. |
| **One open card checkout per order.** A second tap reuses it; a partial unique index (`ux_payments_one_pending_card_per_order`) stops two taps racing. | `StartCardPaymentAsync`, EF config | The farmer must not end up with two ways to pay the same order. |
| **Every path is safe to repeat.** An attempt that has left Pending is final; a repeated webhook, sync or cash click changes nothing. | `ApplyAsync`, `RecordCashPaymentAsync` | Webhooks are delivered *at least once*, and people double-click. |
| **Creating a session carries an Idempotency-Key** (`agriguard-checkout-{paymentId}`), so a retry after a timeout returns the same session. | `StripePaymentGateway` | The HTTP client retries; retries must not open extra sessions. |
| **Webhooks must carry a valid signature,** HMAC-SHA256 over `{timestamp}.{raw body}`, checked in constant time, at most 5 minutes old. | `StripeWebhookSignature` | Without it, anyone who found the URL could post "paid". The age limit stops replays. |
| **Settling runs in a serializable transaction.** If cash is recorded while a card payment goes through, the order is paid once; the second payment is kept as Succeeded but flagged `RefundDue`. | `ApplyAsync`, `RetireAsync` | The money really was taken twice, so it is recorded honestly and marked for a refund, not lost. |
| **Recording cash closes any open card checkout** at Stripe (`expire`), after checking it was not paid in that instant. | `RetireAsync` | Stops the farmer paying again by card after paying cash. |
| **Hand-over needs payment, then the pickup code.** | `OrderService.FulfilAsync` (`ORDER_NOT_PAID`) | Goods leave the shop only once paid, and only to the right farmer. |
| **Live Stripe keys are refused at startup** unless `Payments:Stripe:AllowLiveKeys` is set on purpose. | `PaymentOptionsValidator` | This is an academic project; it must never move real money by mistake. |
| **No Stripe key means card payments are off, not broken.** The phone offers cash only (`canPayByCard: false`); the API answers 503 `CARD_PAYMENTS_UNAVAILABLE`. | `IPaymentGateway.IsConfigured` | CI, the tests and a fresh laptop run without any Stripe account. |

## 3. The data

- `input_orders.payment_status` (Unpaid / Paid) and `paid_at`. Orders already Collected before this change were back-filled as Paid, since they were settled at the counter; they have no payment row, and show "Settled before payments were recorded".
- `payments`: one row per attempt.
  - `method`: Card or Cash.
  - `status`: Pending, Succeeded, Failed, Expired or Cancelled.
  - The amount and currency, copied when the attempt starts.
  - `provider` ("stripe" or "counter"), `provider_reference` (the session id, unique) and `provider_payment_id` (pi_…, the reference for a refund).
  - The card brand and last four, `refund_due`, and `recorded_by_id` (the dealer, for cash).
  - Optimistic concurrency through `xmin`.

## 4. Running it locally

1. Create a free Stripe account, stay in **Test mode**, and copy the **secret key** (`sk_test_…`) from Developers → API keys.
2. In your own terminal: `dotnet user-secrets set "Payments:Stripe:SecretKey" "sk_test_…" --project backend/src/AgriGuard.Api`.
3. Restart the API. The emulator reaches it as `http://10.0.2.2:5000` (`Payments:PublicBaseUrl`), which is where Stripe sends the browser back.
4. Pay with a test card: **4242 4242 4242 4242** (Visa) or **5555 5555 5555 4444** (Mastercard), any future expiry date, any CVC. Try **4000 0000 0000 9995** to see a decline.

The webhook needs a public URL, so it is for the deployed API. In Stripe, add the endpoint
`https://<api>/api/payments/stripe/webhook` with the events `checkout.session.completed`,
`checkout.session.async_payment_succeeded` and `checkout.session.expired`, and set
`Payments__Stripe__WebhookSecret` to its `whsec_…`. Locally, the return page and the app's check
on return confirm the payment without it.

## 5. Likely viva questions

- *Why not let the app tell the server "payment succeeded"?* Because the app can be modified. The server asks Stripe itself.
- *What if the farmer closes the app on Stripe's page?* The attempt stays Pending. The browser's return page, the next opening of My orders (it checks open attempts), or the webhook settles it. If nobody pays, it expires after 30 minutes and a new one can be opened.
- *What if they pay cash and card at the same moment?* Both payments are recorded, the order is paid once, and the later one is flagged `RefundDue`.
- *How is a forged webhook stopped?* By the HMAC signature with the endpoint secret, compared in constant time, plus the 5-minute age limit.
- *Why minor units, and why refuse rounding?* Stripe takes integers in the smallest unit; rounding money silently is a defect.
- *Where would refunds go?* `provider_payment_id` is the Stripe PaymentIntent to refund (`POST v1/refunds`). The refund flow itself is not built; `refund_due` marks what needs one.

Tests: `PaymentTests` (16 integration tests against a fake Stripe that uses the real signature
check), `PaymentRulesTests` and `StripeTests` (unit), the web `OrdersPage.test.tsx` and the phone
`orders_screen_test.dart`.
