using Shared.Kernel.Domain;

namespace UrbanX.Payment.Domain.Models;

public class PaymentProvider : BaseEntity<Guid>
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? Config { get; set; }
    public bool IsActive { get; set; } = true;
    public string[] SupportedCurrencies { get; set; } = ["VND", "USD"];

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
