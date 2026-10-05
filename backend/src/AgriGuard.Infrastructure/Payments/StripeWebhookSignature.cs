using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgriGuard.Application.Payments;

namespace AgriGuard.Infrastructure.Payments;

/// <summary>
/// Proves a webhook came from Stripe. Stripe signs <c>{timestamp}.{raw body}</c> with the endpoint's
/// secret (HMAC-SHA256) and sends <c>Stripe-Signature: t=…,v1=…</c>. Without this check, anyone who
/// found the URL could post "this session is paid".
///
/// - The body must be the raw bytes as received: re-serialising the JSON would change the signature.
/// - The timestamp must be recent, so a captured webhook cannot be replayed later.
/// - The comparison takes the same time however many characters match, so it leaks nothing.
/// </summary>
public static class StripeWebhookSignature
{
    public static void Verify(string payload, string? header, string secret, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(header))
            throw new WebhookSignatureException("The Stripe-Signature header is missing.");

        long? timestamp = null;
        List<string> signatures = [];
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            var (key, value) = (pair[0].Trim(), pair[1].Trim());
            if (key == "t" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
                timestamp = t;
            else if (key == "v1")
                signatures.Add(value);
        }

        if (timestamp is not { } signedAt || signatures.Count == 0)
            throw new WebhookSignatureException("The Stripe-Signature header is malformed.");

        if ((now - DateTimeOffset.FromUnixTimeSeconds(signedAt)).Duration() > tolerance)
            throw new WebhookSignatureException("The webhook is too old or from the future; it may be a replay.");

        var expected = Compute(payload, secret, signedAt);
        foreach (var signature in signatures)
        {
            byte[] given;
            try
            {
                given = Convert.FromHexString(signature);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(expected, given))
                return;
        }

        throw new WebhookSignatureException("The webhook signature does not match.");
    }

    /// <summary>The header Stripe would send for this body at this time. Used by the tests.</summary>
    public static string Header(string payload, string secret, long timestamp) =>
        $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(Compute(payload, secret, timestamp))}";

    private static byte[] Compute(string payload, string secret, long timestamp) =>
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}"));
}
