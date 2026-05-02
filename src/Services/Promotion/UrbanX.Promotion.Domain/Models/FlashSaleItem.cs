using Shared.Kernel.Domain;

namespace UrbanX.Promotion.Domain.Models;

public class FlashSaleItem : BaseEntity<Guid>
{
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public int TotalSlots { get; set; }
    public int SlotsReserved { get; set; }

    private FlashSaleItem() { }

    public static FlashSaleItem Create(Guid promotionId, Guid productId, Guid? variantId, int totalSlots)
    {
        return new FlashSaleItem
        {
            Id = Guid.NewGuid(),
            PromotionId = promotionId,
            ProductId = productId,
            VariantId = variantId,
            TotalSlots = totalSlots,
            SlotsReserved = 0
        };
    }

    public void SyncReservedSlots(int slotsReserved) => SlotsReserved = slotsReserved;
}
