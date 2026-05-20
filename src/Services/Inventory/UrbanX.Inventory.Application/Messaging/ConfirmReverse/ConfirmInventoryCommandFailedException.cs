using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Thrown when <see cref="ConfirmReservationCommand"/> returns failure from the processor pipeline.
/// </summary>
internal sealed class ConfirmInventoryCommandFailedException : Exception
{
    private static readonly HashSet<string> PermanentErrorCodes = new(StringComparer.Ordinal)
    {
        "InventoryReservation.NotFound",
        "InventoryReservation.NotConfirmable",
        "InventoryReservation.InventoryLineMissing",
    };

    public string ErrorCode { get; }

    public bool IsPermanent => PermanentErrorCodes.Contains(ErrorCode);

    public ConfirmInventoryCommandFailedException(Error error)
        : base($"Confirm reservation failed: {error.Code}")
        => ErrorCode = error.Code;
}
