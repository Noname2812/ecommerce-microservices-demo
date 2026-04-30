using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Query
{
    [RequirePermission(Permissions.Products.Read, MinScope = PermissionScope.Own)]
    public record GetVariantDeleteEligibilityQuery(
        Guid ProductId,
        Guid VariantId
    ) : IQuery<VariantDeleteEligibilityResult>;

    public record VariantDeleteEligibilityResult(
        bool CanDelete,
        bool HasActiveReservations,
        bool HasInventoryStock,
        int InventoryQuantity,
        string? BlockReason
    );
}
