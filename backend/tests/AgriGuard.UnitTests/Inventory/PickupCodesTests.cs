using AgriGuard.Domain.Inventory;

namespace AgriGuard.UnitTests.Inventory;

public sealed class PickupCodesTests
{
    [Fact]
    public void A_code_is_six_digits()
    {
        for (var i = 0; i < 50; i++)
            Assert.Matches(@"^\d{6}$", PickupCodes.Generate());
    }

    [Theory]
    [InlineData("482913", true)]
    [InlineData("482 913", true)]
    [InlineData("482-913", true)]
    [InlineData("482914", false)]
    [InlineData("48291", false)]
    [InlineData("4829130", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_order_s_own_code_hands_it_over(string? typed, bool accepted) =>
        Assert.Equal(accepted, PickupCodes.Matches("482913", typed));
}
