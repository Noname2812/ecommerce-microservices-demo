using Shared.Kernel.Primitives;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Domain;

public interface IPaymentRepository
{
    Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentEntity?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<PaymentEntity?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentEntity payment, CancellationToken cancellationToken = default);
    Task<PageResult<PaymentEntity>> ListAsync(int page, int pageSize, string? status, Guid? customerId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
}
