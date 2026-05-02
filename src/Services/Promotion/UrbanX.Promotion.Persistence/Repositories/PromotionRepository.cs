using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class PromotionRepository : IPromotionRepository
{
    private readonly PromotionDbContext _db;

    public PromotionRepository(PromotionDbContext db) => _db = db;

    public async Task<Domain.Models.Promotion?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Promotions
            .Include(p => p.Codes)
            .Include(p => p.FlashSaleItems)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Domain.Models.Promotion?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var upperCode = code.ToUpperInvariant();
        return await _db.Promotions
            .Include(p => p.Codes)
            .Include(p => p.FlashSaleItems)
            .FirstOrDefaultAsync(p => p.Codes.Any(c => c.Code == upperCode), ct);
    }

    public async Task<IReadOnlyList<Domain.Models.Promotion>> GetActiveFlashSalesForItemsAsync(
        IEnumerable<Guid> variantIds, CancellationToken ct = default)
    {
        var variantIdList = variantIds.ToList();
        var now = DateTimeOffset.UtcNow;

        return await _db.Promotions
            .Include(p => p.FlashSaleItems)
            .Where(p => p.Status == PromotionStatus.Active
                     && p.Type == PromotionType.FlashSale
                     && p.StartsAt <= now
                     && (p.EndsAt == null || p.EndsAt > now)
                     && p.FlashSaleItems.Any(f => f.VariantId != null && variantIdList.Contains(f.VariantId.Value)))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Domain.Models.Promotion promotion, CancellationToken ct = default)
    {
        await _db.Promotions.AddAsync(promotion, ct);
    }

    public async Task<(IReadOnlyList<Domain.Models.Promotion> Items, int TotalCount)> ListAsync(
        string? type, string? status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Promotions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.Type == type);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.StartsAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
