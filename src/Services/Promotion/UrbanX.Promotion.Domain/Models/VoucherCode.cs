using Shared.Kernel.Domain;

namespace UrbanX.Promotion.Domain.Models;

public class VoucherCode : BaseEntity<Guid>
{
    public Guid PromotionId { get; set; }
    public string Code { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? AssignedToCustomerId { get; set; }

    private VoucherCode() { }

    public static VoucherCode Create(Guid promotionId, string code, Guid? assignedToCustomerId = null)
    {
        return new VoucherCode
        {
            Id = Guid.NewGuid(),
            PromotionId = promotionId,
            Code = code.ToUpperInvariant(),
            Status = VoucherCodeStatus.Active,
            AssignedToCustomerId = assignedToCustomerId
        };
    }

    public void MarkAsUsed() => Status = VoucherCodeStatus.Used;
    public void Disable() => Status = VoucherCodeStatus.Disabled;
}

public static class VoucherCodeStatus
{
    public const string Active = "ACTIVE";
    public const string Used = "USED";
    public const string Disabled = "DISABLED";
}
