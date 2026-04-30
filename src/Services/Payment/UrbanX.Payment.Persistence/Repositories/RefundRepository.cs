using Microsoft.EntityFrameworkCore;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;

namespace UrbanX.Payment.Persistence.Repositories;

internal sealed class RefundRepository(PaymentDbContext dbContext) : IRefundRepository
{
    public async Task<Refund?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.Refunds
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Refund>> ListByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        await dbContext.Refunds
            .Where(r => r.PaymentId == paymentId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Refund refund, CancellationToken cancellationToken = default) =>
        await dbContext.Refunds.AddAsync(refund, cancellationToken);
}
