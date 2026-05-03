using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Kernel;
using Shared.Kernel.Primitives;

namespace UrbanX.Order.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly OrderDbContext _dbContext;

    private readonly ICompensationCollector _compensation;

    private readonly ILogger<EfUnitOfWork> _logger;

    public EfUnitOfWork(OrderDbContext dbContext, ICompensationCollector compensation, ILogger<EfUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _compensation = compensation;
        _logger = logger;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            _compensation.Clear();

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
                await ExecuteCompensationsAsync();
                throw;
            }
        }, ct);
    }

    private async Task ExecuteCompensationsAsync()
    {
        // Reverse order — LIFO
        var all = _compensation.GetAll().Reverse().ToList();

        foreach (var comp in all)
        {
            try
            {
                // Using CancellationToken.None — Cant cancel interrupt compensation
                await comp.Action(CancellationToken.None);

                _logger.LogInformation(
                    "Compensation executed: {Reason}", comp.Reason);
            }
            catch (Exception ex)
            {
                // Compensation fail → publish event
                _logger.LogError(ex,
                    "Compensation failed: {Reason}. " +
                    "Publishing CompensationFailed event.",
                    comp.Reason);
                // TODO
            }
        }
    }
}
