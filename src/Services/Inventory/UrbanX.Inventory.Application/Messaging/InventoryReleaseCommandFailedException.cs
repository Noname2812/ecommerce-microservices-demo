using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Thrown when <see cref="ReleaseInventoryCommand"/> returns failure from the processor pipeline.
/// Classified as transient by <see cref="InventoryReleaseRequestedConsumer"/> so retries log at warning level, not fatal.
/// </summary>
internal sealed class InventoryReleaseCommandFailedException : Exception
{
    public string ErrorCode { get; }

    public InventoryReleaseCommandFailedException(Error error)
        : base($"Inventory release failed: {error.Code}")
    {
        ErrorCode = error.Code;
    }
}
