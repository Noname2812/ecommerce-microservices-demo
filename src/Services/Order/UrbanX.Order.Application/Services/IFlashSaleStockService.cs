using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Services;

public interface IFlashSaleStockService
{
    Task<Result> TryReserveAsync(Guid saleId, int quantity, CancellationToken ct);

    Task RestoreAsync(Guid saleId, int quantity, CancellationToken ct);
}
