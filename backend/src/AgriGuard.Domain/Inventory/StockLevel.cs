namespace AgriGuard.Domain.Inventory;

public enum StockLevelState { OutOfStock, Low, Sufficient }

/// <summary>
/// Whether a dealer is running out of a product (the low-stock report). Counted in whole packs,
/// because a farmer buys packs: 0.4 kg left of a 1 kg pack is nothing to sell.
/// Only unheld, in-date stock counts.
/// </summary>
public static class StockLevel
{
    /// <summary>Below this many sellable packs a product is flagged, unless the report asks otherwise.</summary>
    public const int DefaultMinPacks = 3;

    public static int SellablePacks(decimal availableQuantity, decimal packSize) =>
        packSize > 0 && availableQuantity > 0 ? (int)Math.Floor(availableQuantity / packSize) : 0;

    public static StockLevelState Classify(int sellablePacks, int minPacks) =>
        sellablePacks <= 0 ? StockLevelState.OutOfStock
        : sellablePacks < minPacks ? StockLevelState.Low
        : StockLevelState.Sufficient;
}
