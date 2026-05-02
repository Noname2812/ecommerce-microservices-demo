using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

public record PreviewDiscountResponse(
    bool IsEligible,
    decimal OrderLevelDiscount,
    IReadOnlyList<ItemDiscount> ItemDiscounts,
    IReadOnlyList<Guid> ApplicablePromotionIds,
    string? IneligibleReason
);
