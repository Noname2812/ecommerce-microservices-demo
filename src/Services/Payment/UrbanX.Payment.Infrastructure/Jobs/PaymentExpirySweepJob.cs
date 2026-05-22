using MediatR;
using Microsoft.Extensions.Logging;
using UrbanX.Payment.Application.Usecases.V1.Command.SweepExpiredPayments;

namespace UrbanX.Payment.Infrastructure.Jobs;

public sealed class PaymentExpirySweepJob(
    ISender sender,
    ILogger<PaymentExpirySweepJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new SweepExpiredPaymentsCommand(), cancellationToken);
        if (result.IsFailure)
            logger.LogWarning("SweepExpiredPayments failed: {Error}", result.Error.Message);
    }
}
