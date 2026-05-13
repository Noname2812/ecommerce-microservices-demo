namespace UrbanX.Order.Infrastructure.Services;

public sealed class SaleAllocationOptions
{
    public const string SectionName = "SaleAllocation";

    /// <summary>Max units per user per campaign enforced by <see cref="SaleAllocationGate"/>.</summary>
    public int DefaultPerUserMax { get; set; } = 5;
}
