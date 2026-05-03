using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly PaymentDbContext _dbContext;

    public EfUnitOfWork(PaymentDbContext dbContext) => _dbContext = dbContext;

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                CancellationToken.None);
            try
            {
                await operation();
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }, ct);
    }
}
