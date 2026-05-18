using FluentValidation.TestHelper;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.PlaceSalesOrder;

public class PlaceSalesOrderCommandValidatorTests
{
    private readonly PlaceSalesOrderCommandValidator _sut = new();

    private static PlaceSalesOrderCommand ValidCommand() => new(
        CampaignId: Guid.NewGuid(),
        ShippingAddress: new("Nguyen Van A", "0912345678",
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null),
        ShippingFee: 30000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString("D"),
        PricingSnapshot: new(DateTimeOffset.UtcNow.AddMinutes(-1)),
        ExpectedTotal: 130_000,
        Items: [new(Guid.NewGuid(), "Product A", null, Guid.NewGuid(),
            "SKU-001", null, Guid.NewGuid(), "Seller A", 100_000, 1, 0, null)]
    );

    [Fact]
    public void CampaignId_Empty_ShouldHaveError()
    {
        var cmd = ValidCommand() with { CampaignId = Guid.Empty };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.CampaignId);
    }

    [Fact]
    public void CampaignId_Valid_ShouldNotHaveError()
    {
        _sut.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.CampaignId);
    }

    [Fact]
    public void ExpectedTotal_Zero_ShouldHaveError()
    {
        var cmd = ValidCommand() with { ExpectedTotal = 0 };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ExpectedTotal);
    }

    [Fact]
    public void ExpectedTotal_Positive_ShouldNotHaveError()
    {
        _sut.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.ExpectedTotal);
    }

    [Fact]
    public void Items_Empty_ShouldHaveError()
    {
        var cmd = ValidCommand() with { Items = [] };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Items_10Items_ShouldNotHaveError()
    {
        var items = Enumerable.Range(0, 10)
            .Select(_ => new PlaceOrderLineDto(Guid.NewGuid(), "Product", null, Guid.NewGuid(),
                "SKU", null, Guid.NewGuid(), "Seller", 100_000, 1, 0, null))
            .ToList();
        var cmd = ValidCommand() with { Items = items };
        _sut.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Items_11Items_ShouldHaveError()
    {
        var items = Enumerable.Range(0, 11)
            .Select(_ => new PlaceOrderLineDto(Guid.NewGuid(), "Product", null, Guid.NewGuid(),
                "SKU", null, Guid.NewGuid(), "Seller", 100_000, 1, 0, null))
            .ToList();
        var cmd = ValidCommand() with { Items = items };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Item_Quantity5_ShouldNotHaveError()
    {
        var cmd = ValidCommand() with
        {
            Items = [new(Guid.NewGuid(), "Product", null, Guid.NewGuid(),
                "SKU", null, Guid.NewGuid(), "Seller", 100_000, 5, 0, null)]
        };
        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Item_Quantity6_ShouldHaveError()
    {
        var cmd = ValidCommand() with
        {
            Items = [new(Guid.NewGuid(), "Product", null, Guid.NewGuid(),
                "SKU", null, Guid.NewGuid(), "Seller", 100_000, 6, 0, null)]
        };
        var result = _sut.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Item_Quantity0_ShouldHaveError()
    {
        var cmd = ValidCommand() with
        {
            Items = [new(Guid.NewGuid(), "Product", null, Guid.NewGuid(),
                "SKU", null, Guid.NewGuid(), "Seller", 100_000, 0, 0, null)]
        };
        var result = _sut.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void IdempotencyKey_ValidUuid_ShouldNotHaveError()
    {
        _sut.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_InvalidString_ShouldHaveError()
    {
        var cmd = ValidCommand() with { IdempotencyKey = "not-a-uuid" };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }
}
