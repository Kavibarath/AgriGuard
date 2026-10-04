using AgriGuard.Domain.Inventory;

namespace AgriGuard.UnitTests.Inventory;

public sealed class StockLevelTests
{
    [Theory]
    [InlineData(0.4, 1, 0)]
    [InlineData(2.5, 1, 2)]
    [InlineData(1.0, 0.25, 4)]
    [InlineData(-1, 1, 0)]
    public void Only_whole_packs_are_sellable(decimal available, decimal packSize, int packs) =>
        Assert.Equal(packs, StockLevel.SellablePacks(available, packSize));

    [Theory]
    [InlineData(0, StockLevelState.OutOfStock)]
    [InlineData(2, StockLevelState.Low)]
    [InlineData(3, StockLevelState.Sufficient)]
    public void Fewer_packs_than_the_minimum_is_low(int packs, StockLevelState state) =>
        Assert.Equal(state, StockLevel.Classify(packs, minPacks: 3));
}
