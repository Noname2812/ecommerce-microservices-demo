using FluentValidation.TestHelper;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.Reserve;

public class ReserveInventoryCommandValidatorTests
{
    private readonly ReserveInventoryCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenIdempotencyKeyEmpty_HasError()
    {
        var cmd = new ReserveInventoryCommand("", [new ReserveInventoryLineItem(Guid.NewGuid(), 1)]);
        var r = _validator.TestValidate(cmd);
        r.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validate_WhenTooManyItems_HasError()
    {
        var key = Guid.NewGuid().ToString();
        var items = Enumerable.Range(0, 51)
            .Select(_ => new ReserveInventoryLineItem(Guid.NewGuid(), 1))
            .ToList();
        var cmd = new ReserveInventoryCommand(key, items);
        var r = _validator.TestValidate(cmd);
        r.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenQuantityOutOfRange_HasError()
    {
        var key = Guid.NewGuid().ToString();
        var cmd = new ReserveInventoryCommand(key, [new ReserveInventoryLineItem(Guid.NewGuid(), 0)]);
        var r = _validator.TestValidate(cmd);
        r.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenProductIdEmpty_HasError()
    {
        var key = Guid.NewGuid().ToString();
        var cmd = new ReserveInventoryCommand(key, [new ReserveInventoryLineItem(Guid.Empty, 1)]);
        var r = _validator.TestValidate(cmd);
        r.ShouldHaveValidationErrorFor("Items[0].ProductId");
    }

    [Fact]
    public void Validate_WhenIdempotencyKeyIsNotGuid_HasError()
    {
        var cmd = new ReserveInventoryCommand("not-a-uuid", [new ReserveInventoryLineItem(Guid.NewGuid(), 1)]);
        var r = _validator.TestValidate(cmd);
        r.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }
}
