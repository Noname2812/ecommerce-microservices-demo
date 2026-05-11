using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;

internal sealed class RefreshProductProjectionCommandHandler(
    IProductRepository productRepo,
    IProductProjectionRepository projectionRepo)
    : ICommandHandler<RefreshProductProjectionCommand>
{
    public async Task<Result> Handle(RefreshProductProjectionCommand cmd, CancellationToken ct)
    {
        var product = await productRepo.GetByIdAsync(cmd.ProductId, ct);
        if (product is null)
            return Result.Success(); // already deleted or not yet visible — skip silently

        await projectionRepo.UpsertAsync(product, ct);
        return Result.Success();
    }
}
