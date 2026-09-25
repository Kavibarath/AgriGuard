namespace AgriGuard.Domain.Inventory;

/// <summary>One batch's unreserved, in-date stock, as seen inside the approval transaction.</summary>
public sealed record BatchStock(Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal Available);

/// <summary>How much to draw from one batch.</summary>
public sealed record BatchDraw(Guid BatchId, decimal Quantity);

/// <summary>
/// Which batches an approved order is drawn from, and how much from each (§9.5).
///
/// First-expiry-first-out: the batch closest to its expiry date goes first. A dealer's oldest stock
/// is used while it is still sellable, rather than new deliveries being sold while old ones expire
/// on the shelf. Ties on expiry break by batch number, so the same stock always gives the same plan.
///
/// Pure: the caller passes batches it has already locked, and applies the plan inside the same
/// transaction. Nothing here reads or writes the database.
/// </summary>
public static class StockAllocation
{
    /// <summary>Whole packs: a farmer buys 2 × 0.25 L, not 0.48 L.</summary>
    public static int PacksFor(decimal quantity, decimal packSize)
    {
        if (packSize <= 0) throw new ArgumentOutOfRangeException(nameof(packSize), packSize, "Pack size must be positive.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        return (int)Math.Ceiling(quantity / packSize);
    }

    /// <summary>
    /// Draws <paramref name="quantity"/> from <paramref name="batches"/>, earliest expiry first.
    /// Returns null when the batches together hold less than that: nothing is drawn partially.
    /// </summary>
    public static IReadOnlyList<BatchDraw>? PlanFefo(IEnumerable<BatchStock> batches, decimal quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");

        var ordered = batches
            .Where(b => b.Available > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNo, StringComparer.Ordinal)
            .ToList();

        if (ordered.Sum(b => b.Available) < quantity)
            return null;

        List<BatchDraw> plan = [];
        var remaining = quantity;
        foreach (var batch in ordered)
        {
            var take = Math.Min(batch.Available, remaining);
            plan.Add(new BatchDraw(batch.BatchId, take));
            remaining -= take;
            if (remaining == 0) break;
        }

        return plan;
    }
}
