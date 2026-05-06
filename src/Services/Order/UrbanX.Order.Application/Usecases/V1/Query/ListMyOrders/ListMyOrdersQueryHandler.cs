using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Query;

public sealed class ListMyOrdersQueryHandler(
    IOrderRepository orderRepository,
    IUserContext userContext)
    : IQueryHandler<ListMyOrdersQuery, PageResult<OrderSummaryDto>>
{
    public async Task<Result<PageResult<OrderSummaryDto>>> Handle(
        ListMyOrdersQuery request, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId!.Value;
        var paged = await orderRepository.GetByUserIdAsync(
            userId, request.Page, request.PageSize, cancellationToken);

        var dtos = paged.Items
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber, o.Status, o.PaymentStatus,
                o.TotalAmount, o.Items.Count, o.CreatedAt))
            .ToList();

        return Result.Success(PageResult<OrderSummaryDto>.Create(
            dtos, paged.PageIndex, paged.PageSize, paged.TotalCount));
    }
}
