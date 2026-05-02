using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class PromotionUsageRepository : IPromotionUsageRepository
{
    private readonly PromotionDbContext _db;

    public PromotionUsageRepository(PromotionDbContext db) => _db = db;

    public async Task<int> CountUsageAsync(Guid promotionId, CancellationToken ct = default)
    {
        return await _db.PromotionUsages
            .CountAsync(u => u.PromotionId == promotionId, ct);
    }

    public async Task<int> CountCustomerUsageAsync(Guid promotionId, Guid customerId, CancellationToken ct = default)
    {
        return await _db.PromotionUsages
            .CountAsync(u => u.PromotionId == promotionId && u.CustomerId == customerId, ct);
    }

    public async Task AddAsync(PromotionUsage usage, CancellationToken ct = default)
    {
        await _db.PromotionUsages.AddAsync(usage, ct);
    }
}
