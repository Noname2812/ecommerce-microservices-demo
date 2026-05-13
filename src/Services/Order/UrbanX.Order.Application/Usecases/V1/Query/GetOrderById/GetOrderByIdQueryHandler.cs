using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Query;

public sealed class GetOrderByIdQueryHandler(
    IOrderRepository orderRepository,
    IUserContext userContext)
    : IQueryHandler<GetOrderByIdQuery, OrderDetailDto>
{
    public async Task<Result<OrderDetailDto>> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure<OrderDetailDto>(OrderErrors.NotFound(request.OrderId));

        var isAdmin = userContext.HasRole(Roles.Admin);
        if (!isAdmin && order.UserId != userContext.UserId)
            return Result.Failure<OrderDetailDto>(OrderErrors.Forbidden);

        return Result.Success(new OrderDetailDto(
            order.Id,
            order.OrderNumber,
            order.UserId,
            order.CustomerEmail,
            order.CustomerName,
            order.Status,
            order.PaymentStatus,
            order.Subtotal,
            order.ShippingFee,
            order.DiscountAmount,
            order.TotalAmount,
            order.CouponCode,
            order.TrackingNumber,
            order.CancelledReason,
            order.CreatedAt,
            order.UpdatedAt,
            new ShippingAddressDto(
                order.ShippingAddress.Street,
                order.ShippingAddress.Ward,
                order.ShippingAddress.District,
                order.ShippingAddress.City,
                order.ShippingAddress.Country,
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.RecipientPhone),
            order.Items.Select(i => new OrderItemDto(
                i.Id, i.ProductId, i.ProductName, i.VariantId, i.VariantSku,
                i.VariantName, i.SellerName, i.UnitPrice, i.Quantity, i.Subtotal,
                i.ImageUrl, i.Status)).ToList(),
            order.StatusHistory
                .OrderBy(h => h.CreatedAt)
                .Select(h => new OrderStatusHistoryDto(h.FromStatus, h.ToStatus, h.Note, h.CreatedAt))
                .ToList()
        ));
    }
}
