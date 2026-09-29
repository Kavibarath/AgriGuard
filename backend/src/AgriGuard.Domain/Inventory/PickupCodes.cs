using System.Security.Cryptography;
using System.Text;

namespace AgriGuard.Domain.Inventory;

/// <summary>
/// The six-digit code a farmer shows the dealer to collect an order. Only the farmer sees it (on
/// the phone); the dealer types it in to mark the order Collected. So the packs, which were bought
/// with a prescription, go to the person the prescription was written for, not to whoever asks
/// at the counter with an order number.
/// </summary>
public static class PickupCodes
{
    public const int Length = 6;

    /// <summary>Cryptographically random, so one order's code says nothing about the next.</summary>
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether what the dealer typed is the order's code. Spaces and dashes are ignored ("482 913"
    /// and "482-913" are how people read a code out). Compared in constant time, so response
    /// timing gives nothing away about how many digits were right.
    /// </summary>
    public static bool Matches(string expected, string? given)
    {
        if (string.IsNullOrWhiteSpace(given))
            return false;

        var cleaned = new string([.. given.Where(ch => ch is not (' ' or '-'))]);
        return cleaned.Length == expected.Length
               && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(cleaned), Encoding.ASCII.GetBytes(expected));
    }
}
