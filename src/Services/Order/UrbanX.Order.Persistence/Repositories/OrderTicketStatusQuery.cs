using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Application.Abstractions;

namespace UrbanX.Order.Persistence.Repositories;

internal sealed class OrderTicketStatusQuery(OrderDbContext db) : IOrderTicketStatusQuery
{
    public async Task<OrderTicketSagaSnapshot?> GetSagaByTicketIdAsync(Guid ticketId, CancellationToken ct = default)
    {
        var normal = await db.PlaceOrderNormalSagas
            .AsNoTracking()
            .Where(s => s.CorrelationId == ticketId)
            .Select(s => new OrderTicketSagaSnapshot(
                s.CurrentState,
                s.FailureReason,
                s.ValidationError,
                s.PaymentExpiresAt))
            .FirstOrDefaultAsync(ct);

        if (normal is not null)
            return normal;

        return await db.PlaceSalesOrderSagas
            .AsNoTracking()
            .Where(s => s.CorrelationId == ticketId)
            .Select(s => new OrderTicketSagaSnapshot(
                s.CurrentState,
                s.FailureReason,
                s.ValidationError,
                s.PaymentExpiresAt))
            .FirstOrDefaultAsync(ct);
    }
}
