using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface IPromotionRepository
{
    Task<Models.Promotion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Models.Promotion?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Models.Promotion>> GetActiveFlashSalesForItemsAsync(IEnumerable<Guid> variantIds, CancellationToken ct = default);
    Task AddAsync(Models.Promotion promotion, CancellationToken ct = default);
    Task<(IReadOnlyList<Models.Promotion> Items, int TotalCount)> ListAsync(
        string? type, string? status, int pageIndex, int pageSize, CancellationToken ct = default);
}
