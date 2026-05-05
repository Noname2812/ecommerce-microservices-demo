using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly PromotionDbContext _dbContext;

    public EfUnitOfWork(PromotionDbContext dbContext) => _dbContext = dbContext;

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async (_) =>
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
