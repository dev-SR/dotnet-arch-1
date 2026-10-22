using FluentAssertions;
using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Products;
using Shared.Common.Errors;

namespace MyApp.Tests.Unit;

public class ProductDomainTests
{
    private static Category Category() => new() { Name = "Gadgets" };
    private static Brand Brand() => new() { Name = "Acme" };

    [Fact]
    public void Create_Valid_ReturnsProduct_AndRaisesDomainEvent()
    {
        var result = Product.Create("Widget", 9.99m, 5, Category(), Brand());

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("Widget");
        result.Value.Price.Should().Be(9.99m);
        result.Value.Stock.Should().Be(5);
        result.Value.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProductCreatedDomainEvent>()
            .Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void Create_ShortName_ReturnsValidation()
    {
        var result = Product.Create("ab", 9.99m, 1, Category(), Brand());

        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "PRODUCT.NAME" && e.Type == ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositivePrice_ReturnsValidation(decimal price)
    {
        var result = Product.Create("Widget", price, 1, Category(), Brand());

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "PRODUCT.PRICE");
    }

    [Fact]
    public void Create_NegativeStock_ReturnsValidation()
    {
        var result = Product.Create("Widget", 1m, -1, Category(), Brand());

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "PRODUCT.STOCK");
    }

    [Fact]
    public void UpdateDetails_Valid_MutatesState()
    {
        var product = Product.Create("Widget", 9.99m, 5, Category(), Brand()).Value;
        product.ClearDomainEvents();

        var updated = product.UpdateDetails("Gadget Pro", 19.5m, 10, "Hardware");

        updated.IsError.Should().BeFalse();
        product.Name.Should().Be("Gadget Pro");
        product.Price.Should().Be(19.5m);
        product.Stock.Should().Be(10);
        product.Category.Name.Should().Be("Hardware");
    }

    [Fact]
    public void UpdateDetails_WhenDeleted_ReturnsConflict()
    {
        var product = Product.Create("Widget", 9.99m, 5, Category(), Brand()).Value;
        product.MarkDeleted().IsError.Should().BeFalse();

        var updated = product.UpdateDetails("X", 1m, 1, "Category");

        updated.IsError.Should().BeTrue();
        updated.Errors.Should().Contain(e => e.Code == "PRODUCT.DELETED" && e.Type == ErrorType.Conflict);
    }

    [Fact]
    public void MarkDeleted_Twice_ReturnsConflict()
    {
        var product = Product.Create("Widget", 9.99m, 5, Category(), Brand()).Value;

        product.MarkDeleted().IsError.Should().BeFalse();
        var second = product.MarkDeleted();

        second.IsError.Should().BeTrue();
        second.Errors.Should().Contain(e => e.Code == "PRODUCT.DELETED");
        product.IsDeleted.Should().BeTrue();
        product.IsActive.Should().BeFalse();
    }
}
