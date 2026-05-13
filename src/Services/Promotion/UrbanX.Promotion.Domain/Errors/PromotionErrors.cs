using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Domain.Errors;

public static class PromotionErrors
{
    public static Error NotFound(Guid id) =>
        new("Promotion.NotFound", $"Promotion {id} was not found");

    public static Error CodeNotFound(string code) =>
        new("Promotion.CodeNotFound", $"Promotion code '{code}' was not found");

    public static readonly Error NotActive =
        new("Promotion.NotActive", "Promotion is not active");

    public static readonly Error Expired =
        new("Promotion.Expired", "Promotion has expired");

    public static readonly Error NotStarted =
        new("Promotion.NotStarted", "Promotion has not started yet");

    public static Error MinOrderAmountNotMet(decimal required, decimal actual) =>
        new("Promotion.MinOrderAmountNotMet",
            $"Minimum order amount of {required:F2} is required (actual: {actual:F2})");

    public static readonly Error UsageLimitReached =
        new("Promotion.UsageLimitReached", "Promotion usage limit has been reached");

    public static readonly Error CustomerUsageLimitReached =
        new("Promotion.CustomerUsageLimitReached", "Customer usage limit for this promotion has been reached");

    public static readonly Error FlashSaleSoldOut =
        new("Promotion.FlashSaleSoldOut", "Flash sale slots have been sold out");

    public static readonly Error ProductNotEligible =
        new("Promotion.ProductNotEligible", "Product is not eligible for this promotion");

    public static Error CodeAlreadyUsed(string code) =>
        new("Promotion.CodeAlreadyUsed", $"Voucher code '{code}' has already been used");

    public static readonly Error InvalidPromotionType =
        new("Promotion.InvalidPromotionType", "Invalid promotion type for this operation");

    public static readonly Error CannotModify =
        new("Promotion.CannotModify", "Promotion can only be modified when in DRAFT status");
}
