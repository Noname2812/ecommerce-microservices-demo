using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Kernel.Exceptions;
using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly InventoryDbContext _dbContext;

    public EfUnitOfWork(InventoryDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    /// <remarks>
    /// Retries <see cref="DbUpdateConcurrencyException"/> (xmin) and PostgreSQL unique violations (idempotency races),
    /// clearing the change tracker between attempts. Final unique violation wraps <see cref="ConcurrencyRetryExhaustedException"/> → 503.
    /// </remarks>
    public async Task ExecuteInTransactionWithConcurrencyRetryAsync(Func<Task> operation, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await ExecuteInTransactionAsync(operation, ct);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (attempt < 2)
                {
                    _dbContext.ChangeTracker.Clear();
                    continue;
                }

                throw new ConcurrencyRetryExhaustedException(
                    "Optimistic concurrency retry exhausted after 3 attempts.", ex);
            }
            catch (DbUpdateException ex) when (IsPostgresUniqueViolation(ex))
            {
                if (attempt < 2)
                {
                    _dbContext.ChangeTracker.Clear();
                    continue;
                }

                throw new ConcurrencyRetryExhaustedException(
                    "Idempotency conflict unresolved after 3 attempts.", ex);
            }
        }
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async (_) =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(CancellationToken.None);
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

    private static bool IsPostgresUniqueViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState == PostgresUniqueViolationSqlState)
                return true;
        }

        return false;
    }
}
