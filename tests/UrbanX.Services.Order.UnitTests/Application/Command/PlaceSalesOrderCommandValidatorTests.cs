using FluentValidation.TestHelper;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

namespace UrbanX.Services.Order.UnitTests.Application.Command;

public sealed class PlaceSalesOrderCommandValidatorTests
{
    private readonly PlaceSalesOrderCommandValidator _sut = new();

    private static PlaceSalesOrderCommand ValidCommand() => new(
        CampaignId: Guid.NewGuid(),
        ShippingAddress: new("Nguyen Van A", "0912345678",
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null),
        ShippingFee: 30_000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString("D"),
        PricingSnapshot: new(DateTimeOffset.UtcNow),
        ExpectedTotal: 130_000,
        Items:
        [
            new(Guid.NewGuid(), "Product A", null, Guid.NewGuid(),
                "SKU-001", null, Guid.NewGuid(), "Seller A", 100_000, 1, 0, null)
        ]);

    [Fact]
    public void ExpectedTotal_Zero_ShouldHaveError() =>
        _sut.TestValidate(ValidCommand() with { ExpectedTotal = 0 })
            .ShouldHaveValidationErrorFor(x => x.ExpectedTotal);

    [Fact]
    public void PricingSnapshot_Required_ShouldNotHaveErrorWhenPresent() =>
        _sut.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.PricingSnapshot);
}
