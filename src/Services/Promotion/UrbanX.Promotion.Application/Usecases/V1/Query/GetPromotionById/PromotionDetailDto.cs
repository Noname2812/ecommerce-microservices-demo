namespace UrbanX.Promotion.Application.Usecases.V1.Query;

public record PromotionDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountCap,
    decimal? MinOrderAmount,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? MaxTotalUsages,
    int? MaxUsagesPerCustomer,
    int UsageCount,
    string Status,
    string TargetScope,
    IReadOnlyList<Guid> TargetIds,
    bool IsStackable,
    IReadOnlyList<VoucherCodeDto> Codes,
    IReadOnlyList<FlashSaleItemDto> FlashSaleItems
);

public record VoucherCodeDto(Guid Id, string Code, string Status, Guid? AssignedToCustomerId);

public record FlashSaleItemDto(Guid Id, Guid ProductId, Guid? VariantId, int TotalSlots, int SlotsReserved);
