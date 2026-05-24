using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;

internal sealed class RefreshProductVariantProjectionCommandHandler(
    IProductVariantReadModelRepository repository,
    ILogger<RefreshProductVariantProjectionCommandHandler> logger)
    : ICommandHandler<RefreshProductVariantProjectionCommand>
{
    public async Task<Result> Handle(RefreshProductVariantProjectionCommand cmd, CancellationToken ct)
    {
        var snapshot = new ProductVariantReadModel
        {
            VariantId = cmd.VariantId,
            ProductId = cmd.ProductId,
            ProductName = cmd.ProductName,
            ProductIsActive = cmd.ProductIsActive,
            Sku = cmd.Sku,
            VariantName = cmd.VariantName,
            ImageUrl = cmd.ImageUrl,
            Price = cmd.Price,
            IsActive = cmd.IsActive,
            SellerId = cmd.SellerId,
            SellerName = cmd.SellerName,
            SellerIsActive = cmd.SellerIsActive,
            RowVersion = cmd.RowVersion,
            ProjectionVersion = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = null
        };

        await repository.UpsertAsync(snapshot, ct);

        logger.LogDebug(
            "Refreshed product variant projection {VariantId} to RowVersion {RowVersion}",
            cmd.VariantId,
            cmd.RowVersion);

        return Result.Success();
    }
}
