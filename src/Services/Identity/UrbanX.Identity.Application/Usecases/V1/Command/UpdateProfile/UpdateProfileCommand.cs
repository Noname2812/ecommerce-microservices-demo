using FluentValidation;
using Shared.Application;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public record UpdateProfileCommand(
    string DisplayName,
    string? PhoneNumber,
    string? AvatarUrl,
    string? Bio,
    DateOnly? DateOfBirth,
    string? Gender,
    string? AddressLine,
    string? City,
    string? Country,
    string? PostalCode
) : ICommand;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(32).Matches(@"^\+?[0-9\s\-]+$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.AvatarUrl)
            .MaximumLength(2048)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl));
        RuleFor(x => x.Bio).MaximumLength(1000);
        RuleFor(x => x.Gender).MaximumLength(20);
        RuleFor(x => x.AddressLine).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(20);
    }
}
