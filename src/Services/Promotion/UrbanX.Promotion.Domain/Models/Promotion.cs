using Shared.Kernel.Domain;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Domain.Models;

public class Promotion : BaseEntity<Guid>
{
    private List<VoucherCode> _codes = [];
    private List<FlashSaleItem> _flashSaleItems = [];

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string DiscountType { get; set; } = null!;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountCap { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int? MaxTotalUsages { get; set; }
    public int? MaxUsagesPerCustomer { get; set; }
    public int UsageCount { get; set; }
    public string Status { get; set; } = null!;
    public string TargetScope { get; set; } = null!;
    public List<Guid> TargetIds { get; set; } = [];
    public bool IsStackable { get; set; }

    public IReadOnlyList<VoucherCode> Codes => _codes.AsReadOnly();
    public IReadOnlyList<FlashSaleItem> FlashSaleItems => _flashSaleItems.AsReadOnly();

    private Promotion() { }

    public static Promotion Create(
        string name,
        string? description,
        string type,
        string discountType,
        decimal discountValue,
        decimal? maxDiscountCap,
        decimal? minOrderAmount,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        int? maxTotalUsages,
        int? maxUsagesPerCustomer,
        string targetScope,
        IEnumerable<Guid>? targetIds,
        bool isStackable)
    {
        return new Promotion
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Type = type,
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountCap = maxDiscountCap,
            MinOrderAmount = minOrderAmount,
            StartsAt = startsAt,
            EndsAt = endsAt,
            MaxTotalUsages = maxTotalUsages,
            MaxUsagesPerCustomer = maxUsagesPerCustomer,
            UsageCount = 0,
            Status = PromotionStatus.Draft,
            TargetScope = targetScope,
            TargetIds = targetIds?.ToList() ?? [],
            IsStackable = isStackable
        };
    }

    public void Activate() => Status = PromotionStatus.Active;
    public void Pause() => Status = PromotionStatus.Paused;
    public void Expire() => Status = PromotionStatus.Expired;
    public void Exhaust() => Status = PromotionStatus.Exhausted;
    public void IncrementUsage() => UsageCount++;

    public void AddCodes(IEnumerable<VoucherCode> codes) => _codes.AddRange(codes);
    public void AddFlashSaleItems(IEnumerable<FlashSaleItem> items) => _flashSaleItems.AddRange(items);
}
