using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Persistence.Repositories;

internal sealed class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public async Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.Payments
            .Include(p => p.Provider)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PaymentEntity?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await dbContext.Payments
            .Include(p => p.Provider)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task<PaymentEntity?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
        await dbContext.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key, cancellationToken);

    public async Task AddAsync(PaymentEntity payment, CancellationToken cancellationToken = default) =>
        await dbContext.Payments.AddAsync(payment, cancellationToken);

    public async Task<PageResult<PaymentEntity>> ListAsync(
        int page, int pageSize, string? status, Guid? customerId,
        DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Payments.Include(p => p.Provider).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);
        if (customerId.HasValue)
            query = query.Where(p => p.CustomerId == customerId.Value);
        if (from.HasValue)
            query = query.Where(p => p.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.CreatedAt <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PageResult<PaymentEntity>.Create(items, page, pageSize, total);
    }
}
