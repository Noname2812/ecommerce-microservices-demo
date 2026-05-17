# TASK-10 — GET /orders/ticket/{ticketId} Polling Endpoint

**Team:** Order · **Effort:** S (0.5d) · **Depends:** TASK-06
**Branch:** `feature/order-refactor/TASK-10-get-by-ticket`

## Mục đích

Client cần polling status sau khi POST 202 nhận `ticketId`. Endpoint trả về:
- `PROCESSING` — saga đang chạy, Order chưa tạo
- `PENDING_PAYMENT` — Order đã tạo + paymentUrl available
- `CONFIRMED` — payment thành công
- `CANCELLED` — failure/timeout

## Files NEW

### `Order.Application/Usecases/V1/Query/GetOrderByTicket/GetOrderByTicketQuery.cs`

```csharp
using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;

[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetOrderByTicketQuery(Guid TicketId) : IQuery<OrderTicketStatusDto>;

public sealed class GetOrderByTicketQueryValidator
    : AbstractValidator<GetOrderByTicketQuery>
{
    public GetOrderByTicketQueryValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
    }
}

public record OrderTicketStatusDto(
    Guid TicketId,
    string Status,                // PROCESSING | PENDING_PAYMENT | CONFIRMED | CANCELLED
    Guid? OrderId,
    string? PaymentUrl,
    string? QrCodeUrl,
    string? PaymentStatus,
    string? CancelledReason,
    DateTimeOffset? PaymentExpiresAt);
```

### `Order.Application/Usecases/V1/Query/GetOrderByTicket/GetOrderByTicketQueryHandler.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Persistence;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;

internal sealed class GetOrderByTicketQueryHandler(
    IOrderRepository orderRepository,
    OrderDbContext dbContext,
    IUserContext userContext)
    : IQueryHandler<GetOrderByTicketQuery, OrderTicketStatusDto>
{
    public async Task<Result<OrderTicketStatusDto>> Handle(
        GetOrderByTicketQuery query, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(query.TicketId, ct);

        // 1. Order đã được saga tạo → trả info từ Order
        if (order is not null)
        {
            // Authorize: chỉ owner hoặc admin
            var userId = userContext.UserId ?? Guid.Empty;
            var isAdmin = userContext.HasRole(Roles.Admin);
            if (!isAdmin && order.UserId != userId)
                return Result.Failure<OrderTicketStatusDto>(OrderErrors.Forbidden);

            return Result.Success(new OrderTicketStatusDto(
                TicketId:          query.TicketId,
                Status:            order.Status,
                OrderId:           order.Id,
                PaymentUrl:        order.PaymentUrl,
                QrCodeUrl:         order.QrCodeUrl,
                PaymentStatus:     order.PaymentStatus,
                CancelledReason:   order.CancelledReason,
                PaymentExpiresAt:  await GetPaymentExpiresAtAsync(query.TicketId, ct)));
        }

        // 2. Order chưa exist → check saga state (cả 2 saga types)
        var normalSaga = await dbContext.Set<PlaceOrderNormalSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == query.TicketId, ct);

        if (normalSaga is not null)
            return BuildFromSagaState(query.TicketId, normalSaga.CurrentState,
                                     normalSaga.FailureReason, normalSaga.ValidationError);

        var salesSaga = await dbContext.Set<PlaceSalesOrderSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == query.TicketId, ct);

        if (salesSaga is not null)
            return BuildFromSagaState(query.TicketId, salesSaga.CurrentState,
                                     salesSaga.FailureReason, salesSaga.ValidationError);

        // 3. Cả Order và saga đều không có → 404
        return Result.Failure<OrderTicketStatusDto>(OrderErrors.TicketNotFound);
    }

    private static Result<OrderTicketStatusDto> BuildFromSagaState(
        Guid ticketId, string currentState, string? failureReason, string? validationError)
    {
        var (status, reason) = currentState switch
        {
            "Faulted" => ("CANCELLED", validationError ?? failureReason ?? "Order failed"),
            _         => ("PROCESSING", (string?)null)
        };

        return Result.Success(new OrderTicketStatusDto(
            TicketId:          ticketId,
            Status:            status,
            OrderId:           status == "CANCELLED" ? null : ticketId,
            PaymentUrl:        null,
            QrCodeUrl:         null,
            PaymentStatus:     null,
            CancelledReason:   reason,
            PaymentExpiresAt:  null));
    }

    private async Task<DateTimeOffset?> GetPaymentExpiresAtAsync(Guid ticketId, CancellationToken ct)
    {
        var normalSaga = await dbContext.Set<PlaceOrderNormalSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == ticketId, ct);
        if (normalSaga?.PaymentExpiresAt is not null) return normalSaga.PaymentExpiresAt;

        var salesSaga = await dbContext.Set<PlaceSalesOrderSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == ticketId, ct);
        return salesSaga?.PaymentExpiresAt;
    }
}
```

### Modify `Order.API/Apis/OrderApis.cs`

Thêm route trong `AddRoutes`:
```csharp
v1.MapGet("/ticket/{ticketId:guid}", GetByTicket);
```

Method:
```csharp
private static async Task<IResult> GetByTicket(
    Guid ticketId, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(new GetOrderByTicketQuery(ticketId), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : ToOrderResult(result);
}
```

Verify `ApiEndpoint.ToOrderResult`:
- `Order.TicketNotFound` → 404
- `Order.Forbidden` → 403

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit tests:
  - Order tồn tại → trả Status=order.Status + PaymentUrl
  - Order không tồn tại, normalSaga active → trả PROCESSING
  - Order không tồn tại, normalSaga Faulted → trả CANCELLED + FailureReason
  - Order không tồn tại, salesSaga active → trả PROCESSING
  - Order không tồn tại, không saga → trả 404 TicketNotFound
  - Order tồn tại nhưng userId khác và không phải Admin → 403 Forbidden
- [ ] Integration test:
  - POST PlaceOrder → poll GET ticket: PROCESSING → PENDING_PAYMENT (sau ~3-5s) → CONFIRMED (sau mock PaymentCompleted)
  - Verify response include `paymentUrl` khi PENDING_PAYMENT
  - Verify response include `cancelledReason` khi CANCELLED

## Notes

- Polling từ client recommended interval: 1-2 giây
- Có thể thêm Redis cache cho Order entity (ttl ngắn 5s) nếu polling load cao — optional, plan này không touch
- Frontend nên implement exponential backoff (1s → 2s → 4s) nếu PROCESSING > 30s
- Authorization scope `MinScope=Own`: user chỉ xem ticket của mình; admin xem được tất cả

## DoD

- [ ] Tests pass
- [ ] PR merge
