using AgriGuard.Domain.Inventory;

namespace AgriGuard.UnitTests.Inventory;

public sealed class PaymentRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Packed)]
    public void An_unpaid_confirmed_or_packed_order_can_be_paid(OrderStatus status) =>
        Assert.Null(PaymentRules.ExplainCannotPay(status, OrderPaymentStatus.Unpaid, 4250m));

    [Theory]
    [InlineData(OrderStatus.Draft, "not confirmed")]
    [InlineData(OrderStatus.Cancelled, "cancelled")]
    [InlineData(OrderStatus.Collected, "already been collected")]
    public void An_order_outside_fulfilment_cannot_be_paid(OrderStatus status, string reason) =>
        Assert.Contains(reason, PaymentRules.ExplainCannotPay(status, OrderPaymentStatus.Unpaid, 4250m));

    [Fact]
    public void A_paid_order_cannot_be_paid_again() =>
        Assert.Contains("already paid", PaymentRules.ExplainCannotPay(OrderStatus.Packed, OrderPaymentStatus.Paid, 4250m));

    [Fact]
    public void An_order_with_nothing_to_pay_cannot_be_paid() =>
        Assert.Contains("nothing to pay", PaymentRules.ExplainCannotPay(OrderStatus.Confirmed, OrderPaymentStatus.Unpaid, 0m));

    [Fact]
    public void Only_a_paid_order_is_handed_over()
    {
        Assert.Null(PaymentRules.ExplainHandOver(OrderPaymentStatus.Paid));
        Assert.Contains("not been paid", PaymentRules.ExplainHandOver(OrderPaymentStatus.Unpaid));
    }

    [Theory]
    [InlineData("4250.50", "LKR", 425050)]
    [InlineData("4250", "LKR", 425000)]
    [InlineData("0.01", "LKR", 1)]
    [InlineData("1200", "JPY", 1200)]
    public void Amounts_go_to_the_provider_in_the_smallest_unit(string amount, string currency, long minor) =>
        Assert.Equal(minor, PaymentRules.ToMinorUnits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency));

    [Fact]
    public void Money_is_never_rounded_into_the_minor_unit()
    {
        Assert.Throws<ArgumentException>(() => PaymentRules.ToMinorUnits(10.005m, "LKR"));
        Assert.Throws<ArgumentException>(() => PaymentRules.ToMinorUnits(10.5m, "JPY"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_payment_must_be_for_more_than_zero(int amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentRules.ToMinorUnits(amount, "LKR"));
}
