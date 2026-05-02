using Shared.Kernel.Domain;

namespace UrbanX.Promotion.Domain.Models;

public class PromotionUsage : BaseEntity<Guid>
{
    public Guid PromotionId { get; set; }
    public Guid? VoucherCodeId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }

    private PromotionUsage() { }

    public static PromotionUsage Record(
        Guid promotionId,
        Guid? voucherCodeId,
        Guid orderId,
        Guid customerId,
        decimal discountAmount)
    {
        return new PromotionUsage
        {
            Id = Guid.NewGuid(),
            PromotionId = promotionId,
            VoucherCodeId = voucherCodeId,
            OrderId = orderId,
            CustomerId = customerId,
            DiscountAmount = discountAmount,
            RedeemedAt = DateTimeOffset.UtcNow
        };
    }
}
