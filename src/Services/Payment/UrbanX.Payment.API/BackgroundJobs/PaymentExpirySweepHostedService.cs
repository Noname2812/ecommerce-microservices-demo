using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Usecases.V1.Command.SweepExpiredPayments;

namespace UrbanX.Payment.API.BackgroundJobs;

internal sealed class PaymentExpirySweepHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SePayOptions> options,
    ILogger<PaymentExpirySweepHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.ExpirySweepInitialDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var sweep = await sender.Send(new SweepExpiredPaymentsCommand(), stoppingToken);
                if (sweep.IsFailure)
                    logger.LogWarning("SweepExpiredPayments failed: {Error}", sweep.Error.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payment expiry sweep failed");
            }

            var o = options.CurrentValue;
            var seconds = Math.Max(o.ExpirySweepMinimumIntervalSeconds, o.ExpirySweepIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
