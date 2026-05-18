using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.Sagas;

namespace UrbanX.Order.Persistence.Repositories;

internal sealed class SalesOrderStatusQuery(OrderDbContext db) : ISalesOrderStatusQuery
{
    public async Task<SalesOrderStatusProjection?> GetAsync(Guid orderId, CancellationToken ct)
    {
        return await (
            from o in db.Orders.AsNoTracking()
            join s in db.PlaceSalesOrderSagas.AsNoTracking()
                on o.Id equals s.OrderId into sj
            from s in sj.DefaultIfEmpty()
            where o.Id == orderId
            select new SalesOrderStatusProjection(
                o.Id,
                o.UserId,
                o.Status,
                o.UpdatedAt,
                (string?)s.CurrentState,
                (Guid?)s.ReservationId,
                // TASK-08: Sales flow now uses Redis Lua coupon lock — no claim id surfaces.
                (Guid?)null,
                s != null && s.CouponLocked,
                (string?)s.FailureStep,
                (string?)s.FailureReason,
                (DateTimeOffset?)s.UpdatedAt)
        ).FirstOrDefaultAsync(ct);
    }
}
