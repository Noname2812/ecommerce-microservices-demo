using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;

public sealed class GetOrderByTicketQueryHandler(
    IOrderRepository orderRepository,
    IOrderTicketStatusQuery ticketStatusQuery,
    IUserContext userContext,
    ICacheService cache,
    IOptions<OrderTicketCacheOptions> cacheOptions)
    : IQueryHandler<GetOrderByTicketQuery, OrderTicketStatusDto>
{
    private readonly OrderTicketCacheOptions _opts = cacheOptions.Value;

    public async Task<Result<OrderTicketStatusDto>> Handle(
        GetOrderByTicketQuery query, CancellationToken ct)
    {
        var cacheKey = CacheKey(query.TicketId);
        var cached = await cache.GetAsync<OrderTicketStatusDto>(cacheKey, ct);
        if (cached is not null)
            return Result.Success(cached);

        var result = await LoadTicketStatusAsync(query, ct);

        if (result.IsSuccess && result.Value is { } dto)
        {
            var ttl = IsTerminal(dto.Status)
                ? TimeSpan.FromSeconds(_opts.TerminalTtlSeconds)
                : TimeSpan.FromSeconds(_opts.NonTerminalTtlSeconds);
            await cache.SetAsync(cacheKey, dto, ttl, ct);
        }

        return result;
    }

    private async Task<Result<OrderTicketStatusDto>> LoadTicketStatusAsync(
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
