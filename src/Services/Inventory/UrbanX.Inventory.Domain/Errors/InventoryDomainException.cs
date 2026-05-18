using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Domain.Errors;

public sealed class InventoryDomainException : DomainException
{
    public InventoryDomainException(string code, string message)
        : base(code, message)
    {
    }
}
