using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Usecases.V1.Command.ExpirePayment;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Command.SweepExpiredPayments;

internal sealed class SweepExpiredPaymentsCommandHandler(
    IPaymentRepository paymentRepository,
    IServiceScopeFactory scopeFactory,
    IOptionsSnapshot<SePayOptions> sePayOptions,
    ILogger<SweepExpiredPaymentsCommandHandler> logger) : ICommandHandler<SweepExpiredPaymentsCommand>
{
    public async Task<Result> Handle(SweepExpiredPaymentsCommand request, CancellationToken cancellationToken)
    {
        var batch = sePayOptions.Value.ExpirySweepBatchSize;
        var ids = await paymentRepository.GetExpiredPaymentIdsAsync(batch, cancellationToken);
        foreach (var id in ids)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var r = await sender.Send(new ExpirePaymentCommand(id), cancellationToken);
            if (r.IsFailure)
                logger.LogWarning("ExpirePayment failed for {PaymentId}: {Error}", id, r.Error.Message);
        }

        return Result.Success();
    }
}
