using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetVariantDeleteEligibility
{
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
