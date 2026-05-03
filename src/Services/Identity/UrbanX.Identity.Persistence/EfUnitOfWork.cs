using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;

namespace UrbanX.Identity.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly IdentityDbContext _dbContext;

    public EfUnitOfWork(IdentityDbContext dbContext) => _dbContext = dbContext;

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
