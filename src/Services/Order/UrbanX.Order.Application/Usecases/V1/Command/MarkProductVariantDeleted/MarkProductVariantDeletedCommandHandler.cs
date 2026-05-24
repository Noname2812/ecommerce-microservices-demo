using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.MarkProductVariantDeleted;

internal sealed class MarkProductVariantDeletedCommandHandler(
    IProductVariantReadModelRepository repository)
    : ICommandHandler<MarkProductVariantDeletedCommand>
{
    public async Task<Result> Handle(MarkProductVariantDeletedCommand cmd, CancellationToken ct)
    {
        await repository.MarkDeletedAsync(cmd.VariantId, DateTimeOffset.UtcNow, ct);
        return Result.Success();
    }
}
