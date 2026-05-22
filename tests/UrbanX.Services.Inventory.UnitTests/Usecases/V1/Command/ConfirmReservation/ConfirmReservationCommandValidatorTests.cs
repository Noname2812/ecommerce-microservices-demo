using FluentValidation.TestHelper;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public sealed class ConfirmReservationCommandValidatorTests
{
    private readonly ConfirmReservationCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenOrderIdEmpty_HasValidationError()
    {
        var result = _validator.TestValidate(new ConfirmReservationCommand(Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }
}
