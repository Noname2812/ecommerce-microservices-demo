using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Domain;

public interface IPaymentProviderRepository
{
    Task<PaymentProvider?> GetActiveByTypeAsync(string type, CancellationToken cancellationToken = default);
}
