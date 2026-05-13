using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Domain;

public interface IPaymentEventRepository
{
    Task<bool> ExistsByExternalTransactionIdAsync(string externalTransactionId, CancellationToken cancellationToken = default);

    Task AddAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken = default);
}
