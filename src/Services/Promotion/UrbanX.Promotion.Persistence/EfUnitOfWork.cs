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

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                await operation();
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
