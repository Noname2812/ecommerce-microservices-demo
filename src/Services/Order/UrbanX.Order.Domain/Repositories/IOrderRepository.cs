using Shared.Kernel.Primitives;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Domain.Repositories;

public interface IOrderRepository
{
    Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderEntity?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<PageResult<OrderEntity>> GetByCustomerIdAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default);
    void Add(OrderEntity order);
}
