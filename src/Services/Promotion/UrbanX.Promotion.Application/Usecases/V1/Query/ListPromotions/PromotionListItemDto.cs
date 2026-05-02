namespace UrbanX.Promotion.Application.Usecases.V1.Query;

public record PromotionListItemDto(
    Guid Id,
    string Name,
    string Type,
    string DiscountType,
    decimal DiscountValue,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int UsageCount,
    int? MaxTotalUsages
);
