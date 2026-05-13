using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Application.Abstractions;
using UrbanX.Catalog.Domain.Errors;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query
{
    public sealed class GetVariantDeleteEligibilityQueryHandler
        : IQueryHandler<GetVariantDeleteEligibilityQuery, VariantDeleteEligibilityResult>
    {
        private readonly IProductRepository _productRepository;
        private readonly IInventoryServiceClient _inventoryServiceClient;
        private readonly IUserContext _userContext;

        public GetVariantDeleteEligibilityQueryHandler(
            IProductRepository productRepository,
            IInventoryServiceClient inventoryServiceClient,
            IUserContext userContext)
        {
            _productRepository = productRepository;
            _inventoryServiceClient = inventoryServiceClient;
            _userContext = userContext;
        }

        public async Task<Result<VariantDeleteEligibilityResult>> Handle(
            GetVariantDeleteEligibilityQuery request,
            CancellationToken cancellationToken)
        {
            var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure<VariantDeleteEligibilityResult>(CatalogErrors.ProductNotFound(request.ProductId));

            if (_userContext.Scope == PermissionScope.Own && product.SellerId != _userContext.UserId)
                return Result.Failure<VariantDeleteEligibilityResult>(CatalogErrors.Forbidden());

            var variant = product.Variants.FirstOrDefault(v => v.Id == request.VariantId && v.DeletedAt == null);
            if (variant is null)
                return Result.Failure<VariantDeleteEligibilityResult>(CatalogErrors.VariantNotFound(request.VariantId));

            var activeVariants = product.Variants.Where(v => v.DeletedAt == null && v.IsActive).ToList();
            if (activeVariants.Count == 1 && activeVariants[0].Id == request.VariantId)
            {
                return Result.Success(new VariantDeleteEligibilityResult(
                    CanDelete: false,
                    HasActiveReservations: false,
                    HasInventoryStock: false,
                    InventoryQuantity: 0,
                    BlockReason: "Cannot delete the last active variant"));
            }

            var invStatus = await _inventoryServiceClient.GetVariantInventoryStatusAsync(request.VariantId, cancellationToken);
            if (invStatus is null)
            {
                return Result.Success(new VariantDeleteEligibilityResult(
                    CanDelete: false,
                    HasActiveReservations: false,
                    HasInventoryStock: false,
                    InventoryQuantity: 0,
                    BlockReason: "Cannot confirm reservation state — inventory service unavailable"));
            }

            if (invStatus.HasActiveReservations)
            {
                return Result.Success(new VariantDeleteEligibilityResult(
                    CanDelete: false,
                    HasActiveReservations: true,
                    HasInventoryStock: invStatus.Quantity > 0,
                    InventoryQuantity: invStatus.Quantity,
                    BlockReason: "Variant has active reservations"));
            }

            return Result.Success(new VariantDeleteEligibilityResult(
                CanDelete: true,
                HasActiveReservations: false,
                HasInventoryStock: invStatus.Quantity > 0,
                InventoryQuantity: invStatus.Quantity,
                BlockReason: null));
        }
    }
}
