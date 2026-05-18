using FluentValidation.TestHelper;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public class ConfirmReservationCommandValidatorTests
{
    private readonly ConfirmReservationCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenReservationIdEmpty_HasValidationError()
    {
        var result = _validator.TestValidate(
            new ConfirmReservationCommand(Guid.Empty, "key"));

        result.ShouldHaveValidationErrorFor(x => x.ReservationId);
    }

    [Fact]
    public void Validate_WhenIdempotencyKeyEmpty_HasValidationError()
    {
        var result = _validator.TestValidate(
            new ConfirmReservationCommand(Guid.NewGuid(), string.Empty));

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validate_WhenIdempotencyKeyExceedsMaxLength_HasValidationError()
    {
        var result = _validator.TestValidate(
            new ConfirmReservationCommand(Guid.NewGuid(), new string('x', 101)));

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validate_WhenEventIdIsEmptyGuid_HasValidationError()
    {
        var result = _validator.TestValidate(
            new ConfirmReservationCommand(Guid.NewGuid(), "key", Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.EventId!.Value);
    }

    [Fact]
    public void Validate_WhenEventIdIsNull_IsValid()
    {
        var result = _validator.TestValidate(
            new ConfirmReservationCommand(Guid.NewGuid(), "key", EventId: null));

        result.ShouldNotHaveValidationErrorFor(x => x.EventId!.Value);
    }
}
