using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Services;

public interface ISaleEligibilityService
{
    Task<Result<SaleEligibility>> ValidateAsync(
        Guid campaignId,
        Guid userId,
        IReadOnlyList<SaleEligibilityItem> items,
        CancellationToken ct);
}

public sealed record SaleEligibilityItem(Guid ProductId, Guid VariantId, int Quantity, decimal UnitPrice);

public sealed record SaleEligibility(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal SaleDiscountAmount,
    string DiscountType);
