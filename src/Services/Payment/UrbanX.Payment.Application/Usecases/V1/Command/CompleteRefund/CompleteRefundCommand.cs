using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CompleteRefund;

[RequirePermission(Permissions.Payment.Write)]
public record CompleteRefundCommand(
    Guid RefundId,
    string? ProviderRefundId = null
) : ICommand;

public sealed class CompleteRefundCommandValidator : AbstractValidator<CompleteRefundCommand>
{
    public CompleteRefundCommandValidator()
    {
        RuleFor(x => x.RefundId).NotEmpty();
    }
}
