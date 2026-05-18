using Shared.Kernel.Primitives;

namespace UrbanX.Order.Domain.Exceptions;

public static class OrderExceptions
{
    public sealed class CannotMarkPaidInStatus : DomainException
    {
        public CannotMarkPaidInStatus(string status)
            : base("Order.InvalidStatus", $"Cannot mark paid in status {status}") { }
    }
}
