using FluentValidation.TestHelper;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.Reserve;

public sealed class ReserveInventoryCommandValidatorTests
{
    private readonly ReserveInventoryCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenOrderIdEmpty_HasError()
    {
        var cmd = new ReserveInventoryCommand(Guid.Empty, 15, [new ReserveInventoryLineItem(Guid.NewGuid(), 1)]);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_WhenTooManyItems_HasError()
    {
        var items = Enumerable.Range(0, 51)
            .Select(_ => new ReserveInventoryLineItem(Guid.NewGuid(), 1))
            .ToList();
        var cmd = new ReserveInventoryCommand(Guid.NewGuid(), 15, items);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenVariantIdEmpty_HasError()
    {
        var cmd = new ReserveInventoryCommand(Guid.NewGuid(), 15, [new ReserveInventoryLineItem(Guid.Empty, 1)]);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("Items[0].VariantId");
    }
}
