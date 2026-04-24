using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.Catalog.Persistence;

namespace UrbanX.Catalog.Application.Behaviors
{
    internal sealed class CatalogTransactionBehavior<TRequest, TResponse>(
        CatalogDbContext dbContext,
        ILogger<CatalogTransactionBehavior<TRequest, TResponse>> logger)
        : TransactionPipelineBehavior<TRequest, TResponse, CatalogDbContext>(dbContext, logger)
        where TRequest : ICommandBase
        where TResponse : notnull;
}
