using Microsoft.EntityFrameworkCore;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Persistence.Repositories;

internal sealed class PaymentEventRepository(PaymentDbContext dbContext) : IPaymentEventRepository
{
    public Task<bool> ExistsByExternalTransactionIdAsync(string externalTransactionId, CancellationToken cancellationToken = default) =>
        dbContext.PaymentEvents.AnyAsync(e => e.ExternalTransactionId == externalTransactionId, cancellationToken);

    public async Task AddAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken = default) =>
        await dbContext.PaymentEvents.AddAsync(paymentEvent, cancellationToken);
}
