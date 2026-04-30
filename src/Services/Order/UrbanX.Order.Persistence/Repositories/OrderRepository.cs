using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Repositories;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence.Repositories;

internal sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public async Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt == null, ct);

    public async Task<OrderEntity?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        await db.Orders
            .FirstOrDefaultAsync(o => o.IdempotencyKey == key, ct);

    public async Task<PageResult<OrderEntity>> GetByCustomerIdAsync(
        Guid customerId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.DeletedAt == null)
            .OrderByDescending(o => o.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PageResult<OrderEntity>.Create(items, page, pageSize, total);
    }

    public void Add(OrderEntity order) => db.Orders.Add(order);
}
