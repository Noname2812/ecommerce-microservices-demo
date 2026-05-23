using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;

public sealed class GetOrderByTicketQueryHandler(
    IOrderRepository orderRepository,
    IOrderTicketStatusQuery ticketStatusQuery,
    IUserContext userContext,
    ICacheService cache,
    IOrderStatusCachePolicy cachePolicy)
    : IQueryHandler<GetOrderByTicketQuery, OrderTicketStatusDto>
{
    public async Task<Result<OrderTicketStatusDto>> Handle(
        GetOrderByTicketQuery query, CancellationToken ct)
    {
        var cacheKey = CacheKey(query.TicketId);

        var options = new GetOrSetOptions<Result<OrderTicketStatusDto>>
        {
            UseSingleFlight = true,
            // Polling TTL is short (2s default for non-terminal) — distributed lock
            // overhead exceeds the benefit of cross-process deduplication.
            UseDistributedLock = false,
            ExpirySelector = ResolveTtl,
        };

        var result = await cache.GetOrSetAsync(
            cacheKey,
            innerCt => LoadTicketStatusAsync(query, innerCt),
            options,
            ct);

        return result ?? Result.Failure<OrderTicketStatusDto>(OrderErrors.TicketNotFound);
    }

    private TimeSpan ResolveTtl(Result<OrderTicketStatusDto> result)
    {
        if (result.IsSuccess && result.Value is { } dto && IsTerminal(dto.Status))
            return cachePolicy.TerminalTtl;

        return cachePolicy.NonTerminalTtl;
    }

    private async Task<Result<OrderTicketStatusDto>?> LoadTicketStatusAsync(
        GetOrderByTicketQuery query, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(query.TicketId, ct);

        if (order is not null)
        {
            var userId = userContext.UserId ?? Guid.Empty;
            var isAdmin = userContext.HasRole(Roles.Admin);
            if (!isAdmin && order.UserId != userId)
                return Result.Failure<OrderTicketStatusDto>(OrderErrors.Forbidden);

            var saga = await ticketStatusQuery.GetSagaByTicketIdAsync(query.TicketId, ct);

            return Result.Success(new OrderTicketStatusDto(
                TicketId:         query.TicketId,
                Status:           order.Status,
                OrderId:          order.Id,
                PaymentUrl:       order.PaymentUrl,
                QrCodeUrl:        order.QrCodeUrl,
                PaymentStatus:    order.PaymentStatus,
                CancelledReason:  order.CancelledReason,
                PaymentExpiresAt: saga?.PaymentExpiresAt));
        }

        var sagaOnly = await ticketStatusQuery.GetSagaByTicketIdAsync(query.TicketId, ct);
        if (sagaOnly is not null)
            return BuildFromSagaState(query.TicketId, sagaOnly);

        return Result.Failure<OrderTicketStatusDto>(OrderErrors.TicketNotFound);
    }

    private static Result<OrderTicketStatusDto> BuildFromSagaState(
        Guid ticketId, OrderTicketSagaSnapshot saga)
    {
        var (status, reason) = saga.CurrentState switch
        {
            "Faulted" => ("CANCELLED", saga.ValidationError ?? saga.FailureReason ?? "Order failed"),
            _         => ("PROCESSING", (string?)null)
        };

        return Result.Success(new OrderTicketStatusDto(
            TicketId:         ticketId,
            Status:           status,
            OrderId:          status == "CANCELLED" ? null : ticketId,
            PaymentUrl:       null,
            QrCodeUrl:        null,
            PaymentStatus:    null,
            CancelledReason:  reason,
            PaymentExpiresAt: null));
    }

    private static string CacheKey(Guid ticketId) => $"order:ticket:{ticketId}";

    private static bool IsTerminal(string status) =>
        status == OrderStatus.Confirmed || status == OrderStatus.Cancelled;
}
