using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Domain;

public interface IRefundRepository
{
    Task<Refund?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Refund>> ListByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task AddAsync(Refund refund, CancellationToken cancellationToken = default);
}
