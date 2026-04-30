using Microsoft.EntityFrameworkCore;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Persistence.Repositories;

internal sealed class PaymentProviderRepository(PaymentDbContext dbContext) : IPaymentProviderRepository
{
    public async Task<PaymentProvider?> GetActiveByTypeAsync(string type, CancellationToken cancellationToken = default) =>
        await dbContext.PaymentProviders
            .FirstOrDefaultAsync(p => p.Type == type && p.IsActive, cancellationToken);
}
