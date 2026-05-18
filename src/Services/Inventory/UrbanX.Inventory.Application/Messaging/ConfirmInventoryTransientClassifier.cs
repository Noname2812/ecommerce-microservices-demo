using Shared.Kernel.Exceptions;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Transient vs permanent classification for <see cref="ConfirmInventoryRequestedConsumer"/>.
/// Shared with unit tests via <c>InternalsVisibleTo</c> (no reflection on protected overrides).
/// </summary>
internal static class ConfirmInventoryTransientClassifier
{
    public static bool IsTransient(Exception ex, Func<Exception, bool> defaultClassifier)
    {
        return ex switch
        {
            ConfirmInventoryCommandFailedException confirm => !confirm.IsPermanent,
            ConcurrencyRetryExhaustedException => true,
            _ => defaultClassifier(ex)
        };
    }
}
