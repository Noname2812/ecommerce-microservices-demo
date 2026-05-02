using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface IPromotionUsageRepository
{
    Task<int> CountUsageAsync(Guid promotionId, CancellationToken ct = default);
    Task<int> CountCustomerUsageAsync(Guid promotionId, Guid customerId, CancellationToken ct = default);
    Task AddAsync(PromotionUsage usage, CancellationToken ct = default);
}
