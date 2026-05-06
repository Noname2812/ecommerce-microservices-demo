using FluentValidation.TestHelper;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Services.Promotion.UnitTests.Usecases.V1.Command.ReleaseCouponClaim;

public sealed class ReleaseCouponClaimCommandValidatorTests
{
    private readonly ReleaseCouponClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenClaimIdEmpty_HasValidationError()
    {
        // Arrange
        var command = new ReleaseCouponClaimCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ClaimId);
    }

    [Fact]
    public void Validate_WhenClaimIdProvided_HasNoErrors()
    {
        // Arrange
        var command = new ReleaseCouponClaimCommand(Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
