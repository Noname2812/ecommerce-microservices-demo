using FluentValidation.TestHelper;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceOrder;

public class PlaceOrderCommandValidatorTests
{
    private readonly PlaceOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenCommandIsValid_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenUserIdEmpty_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { UserId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Validate_WhenItemsEmpty_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Items = [] });
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenItemsExceedLimit_HasError()
    {
        var items = Enumerable.Range(0, 21).Select(_ => ValidItem()).ToList();
        var result = _validator.TestValidate(ValidCommand() with { Items = items });
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_WhenQuantityOutOfRange_HasError(int quantity)
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            Items = [ValidItem() with { Quantity = quantity }]
        });
        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenProductIdEmpty_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            Items = [ValidItem() with { ProductId = Guid.Empty }]
        });
        result.ShouldHaveValidationErrorFor("Items[0].ProductId");
    }

    [Fact]
    public void Validate_WhenShippingAddressMissingRequiredFields_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            ShippingAddress = ValidAddress() with
            {
                FullName = "",
                Address = "",
                District = "",
                City = ""
            }
        });

        Assert.Contains(result.Errors, e => e.PropertyName.Contains("ShippingAddress"));
    }

    [Fact]
    public void Validate_WhenPhoneInvalid_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            ShippingAddress = ValidAddress() with { Phone = "abc123" }
        });
        result.ShouldHaveValidationErrorFor("ShippingAddress.Phone");
    }

    [Fact]
    public void Validate_WhenCouponCodeInvalid_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { CouponCode = "invalid code" });
        result.ShouldHaveValidationErrorFor(x => x.CouponCode);
    }

    [Fact]
    public void Validate_WhenSnapshotTooOld_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            PricingSnapshot = new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow.AddMinutes(-31))
        });
        result.ShouldHaveValidationErrorFor(x => x.PricingSnapshot.CapturedAt);
    }

    private static PlaceOrderCommand ValidCommand() => new(
        UserId: Guid.NewGuid(),
        ShippingAddress: ValidAddress(),
        ShippingFee: 25_000,
        CouponCode: "SPRING-2026",
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString(),
        PricingSnapshot: new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
        Items: [ValidItem()]);

    private static PlaceOrderShippingAddressDto ValidAddress() => new(
        FullName: "UrbanX User",
        Phone: "+84987654321",
        Address: "123 Main St",
        Ward: "Ward 1",
        District: "District 1",
        City: "HoChiMinh",
        Province: null,
        Country: "VN",
        ZipCode: "700000");

    private static PlaceOrderLineDto ValidItem() => new(
        ProductId: Guid.NewGuid(),
        ProductName: "Product",
        ProductSlug: "product",
        VariantId: Guid.NewGuid(),
        VariantSku: "SKU-1",
        VariantName: "Default",
        SellerId: Guid.NewGuid(),
        SellerName: "Seller",
        UnitPrice: 100_000,
        Quantity: 1,
        DiscountAmount: 0,
        ImageUrl: null);
}
