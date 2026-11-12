using FluentAssertions;
using MyApp.OrdersApi.Orders;
using MyApp.OrdersApi.Orders.PlaceOrder.Events;
using Shared.Common.Errors;

namespace MyApp.Tests.Unit;

public class OrderPlaceTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    private static OrderLine Line(int quantity = 2, decimal price = 10m) =>
        new(Guid.NewGuid(), "Widget", quantity, price);

    [Fact]
    public void Place_derives_total_from_lines_and_marks_placed()
    {
        var result = Order.Place(Customer, [Line(quantity: 2, price: 10m), Line(quantity: 3, price: 5m)]);

        result.IsError.Should().BeFalse();
        result.Value.Total.Should().Be(35m);
        result.Value.Status.Should().Be(OrderStatus.Placed);
        result.Value.CustomerId.Should().Be(Customer);
        result.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Place_raises_one_order_placed_event_with_the_snapshot()
    {
        var result = Order.Place(Customer, [Line(quantity: 1, price: 7m)]);

        result.Value.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderPlacedDomainEvent>()
            .Which.Lines.Should().ContainSingle()
            .Which.Quantity.Should().Be(1);
    }

    [Fact]
    public void Place_rejects_an_empty_order()
    {
        var result = Order.Place(Customer, []);

        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "ORDER.EMPTY" && e.Type == ErrorType.Validation);
    }

    [Fact]
    public void Place_rejects_non_positive_quantity_and_negative_price()
    {
        var result = Order.Place(Customer, [Line(quantity: 0), Line(price: -1m)]);

        result.IsError.Should().BeTrue();
        result.Errors.Select(e => e.Code).Should().Contain(["ORDER.QUANTITY", "ORDER.PRICE"]);
    }

    [Fact]
    public void Place_rejects_an_empty_customer()
    {
        var result = Order.Place(Guid.Empty, [Line()]);

        result.Errors.Should().ContainSingle(e => e.Code == "ORDER.CUSTOMER");
    }
}
