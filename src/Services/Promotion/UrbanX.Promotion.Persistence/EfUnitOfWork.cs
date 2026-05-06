using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Application.Logging;

namespace UrbanX.Promotion.Persistence;

public sealed class EfUnitOfWork(
    PromotionDbContext dbContext,
    IPostCommitTaskQueue postCommitTasks,
    ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    private readonly PromotionDbContext _dbContext = dbContext;
    private readonly IPostCommitTaskQueue _postCommitTasks = postCommitTasks;
    private readonly ILogger<EfUnitOfWork> _logger = logger;

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async (token) =>
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
                    _postCommitTasks.DiscardPending();
                    throw;
                }

                try
                {
                    await _postCommitTasks.FlushAsync(token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        PromotionLogEvents.PromotionPostCommitBatchFailed,
                        ex,
                        "Post-commit task batch failed after SQL commit (Promotion). Check earlier logs for CouponClaimRedisPostCommitFailed — DB may already reflect released state.");
                    throw;
                }
            },
            ct);
    }
}
