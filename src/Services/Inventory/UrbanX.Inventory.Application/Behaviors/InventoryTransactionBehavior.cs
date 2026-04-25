using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.Inventory.Persistence;

namespace UrbanX.Inventory.Application.Behaviors;

internal sealed class InventoryTransactionBehavior<TRequest, TResponse>(
    InventoryDbContext dbContext,
    ILogger<InventoryTransactionBehavior<TRequest, TResponse>> logger)
    : TransactionPipelineBehavior<TRequest, TResponse, InventoryDbContext>(dbContext, logger)
    where TRequest : ICommandBase
    where TResponse : notnull;
