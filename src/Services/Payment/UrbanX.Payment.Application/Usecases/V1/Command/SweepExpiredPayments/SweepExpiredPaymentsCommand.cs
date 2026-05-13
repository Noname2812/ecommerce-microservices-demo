using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.SweepExpiredPayments;

[AllowAnonymous]
public sealed record SweepExpiredPaymentsCommand : ICommand;

public sealed class SweepExpiredPaymentsCommandValidator : AbstractValidator<SweepExpiredPaymentsCommand>
{
    public SweepExpiredPaymentsCommandValidator() { }
}
