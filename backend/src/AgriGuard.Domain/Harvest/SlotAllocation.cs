namespace AgriGuard.Domain.Harvest;

/// <summary>A collection slot as the allocation sees it: when, where, and how much room is left.</summary>
public sealed record SlotOption(
    Guid SlotId,
    Guid CentreId,
    DateOnly Date,
    int SlotIndex,
    decimal RemainingKg,
    decimal CentreLatitude,
    decimal CentreLongitude);

/// <summary>
/// Which slot a harvest goes to (Component D's capacity-constrained allocation, §5.1).
///
/// A delivery is never split: one farmer's harvest goes to one slot that has room for all of it.
/// Among slots with room, the order of preference is:
///   1. the farmer's preferred date, then each later day in turn (never earlier: the crop may not
///      be safe to harvest yet);
///   2. on the same day, the nearest centre to the plot (straight-line distance);
///   3. then the earliest slot of that day.
///
/// Pure: the caller passes slots it has already locked and applies the choice in the same
/// transaction, like StockAllocation in Component C.
/// </summary>
public static class SlotAllocation
{
    /// <summary>How many days after the preferred date a farmer's harvest may slip.</summary>
    public const int MaxDaysLater = 2;

    public static SlotOption? Choose(IEnumerable<SlotOption> slots, decimal quantityKg, DateOnly preferredDate, decimal plotLatitude, decimal plotLongitude)
    {
        if (quantityKg <= 0) throw new ArgumentOutOfRangeException(nameof(quantityKg), quantityKg, "Quantity must be positive.");

        return slots
            .Where(s => s.RemainingKg >= quantityKg && s.Date >= preferredDate && s.Date <= preferredDate.AddDays(MaxDaysLater))
            .OrderBy(s => s.Date)
            .ThenBy(s => DistanceKm(plotLatitude, plotLongitude, s.CentreLatitude, s.CentreLongitude))
            .ThenBy(s => s.SlotIndex)
            .FirstOrDefault();
    }

    /// <summary>Great-circle distance (haversine), in kilometres.</summary>
    public static double DistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double earthRadiusKm = 6371;
        static double Rad(decimal degrees) => (double)degrees * Math.PI / 180;

        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
