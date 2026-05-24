using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.UpdateProductStatusProjection;

internal sealed class UpdateProductStatusProjectionCommandHandler(
    IProductVariantReadModelRepository repository)
    : ICommandHandler<UpdateProductStatusProjectionCommand>
{
    public async Task<Result> Handle(UpdateProductStatusProjectionCommand cmd, CancellationToken ct)
    {
        await repository.UpdateProductStatusAsync(cmd.ProductId, cmd.IsActive, DateTimeOffset.UtcNow, ct);
        return Result.Success();
    }
}
