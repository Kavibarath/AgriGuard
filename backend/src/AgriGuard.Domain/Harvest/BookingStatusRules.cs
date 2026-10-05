namespace AgriGuard.Domain.Harvest;

/// <summary>
/// What happens to a booking at the collection centre, recorded by co-op staff:
///
///   Booked ──check in──► CheckedIn ──weigh──► Completed (with the weight actually delivered)
///     │
///     └──── missed ────► NoShow
///
/// A farmer cancels their own booking before the day (Cancelled, elsewhere). Nothing here can be
/// recorded before the collection day, and nothing moves backwards: a weighed delivery stays weighed.
/// Pure domain logic with no EF or HTTP dependency, so it is unit-tested directly.
/// </summary>
public static class BookingStatusRules
{
    /// <summary>The statuses staff can record. Booked is where every booking starts; Cancelled is the farmer's.</summary>
    public static readonly IReadOnlySet<BookingStatus> Recordable =
        new HashSet<BookingStatus> { BookingStatus.CheckedIn, BookingStatus.Completed, BookingStatus.NoShow };

    /// <summary>The one status each recorded step comes from.</summary>
    private static readonly IReadOnlyDictionary<BookingStatus, BookingStatus> From = new Dictionary<BookingStatus, BookingStatus>
    {
        [BookingStatus.CheckedIn] = BookingStatus.Booked,
        [BookingStatus.Completed] = BookingStatus.CheckedIn,
        [BookingStatus.NoShow] = BookingStatus.Booked
    };

    /// <summary>
    /// Why a booking may not move from <paramref name="from"/> to <paramref name="to"/> on
    /// <paramref name="today"/>, or null when it may. The message reaches centre staff, so it says
    /// what to do instead.
    /// </summary>
    public static string? ExplainRecord(BookingStatus from, BookingStatus to, DateOnly slotDate, DateOnly today)
    {
        if (!Recordable.Contains(to))
            return $"{to} is not recorded at the centre.";

        if (from is BookingStatus.Cancelled)
            return "The farmer cancelled this booking.";
        if (from is BookingStatus.Completed)
            return "This delivery has already been weighed and completed.";
        if (from is BookingStatus.NoShow && to is not BookingStatus.NoShow)
            return "This booking was marked missed. The farmer must book again.";

        if (slotDate > today)
            return $"The collection day is {slotDate:yyyy-MM-dd}; nothing can be recorded before then.";

        if (From[to] != from)
            return to switch
            {
                BookingStatus.Completed => "Check the farmer in before recording the weight.",
                BookingStatus.NoShow => "The farmer has already checked in; record the weight instead.",
                _ => $"A {from} booking cannot be checked in."
            };

        return null;
    }

    /// <summary>Why a delivered weight is not acceptable, or null. Zero is allowed: the farmer came with nothing to sell.</summary>
    public static string? ExplainWeight(decimal? actualQuantityKg) => actualQuantityKg switch
    {
        null => "Enter the weight delivered, in kg.",
        < 0 => "The weight cannot be negative.",
        > 100_000 => "That is more than any centre takes in a day. Check the weight.",
        _ => null
    };
}
