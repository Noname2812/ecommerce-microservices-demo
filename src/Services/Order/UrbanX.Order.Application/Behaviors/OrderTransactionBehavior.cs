using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.Order.Persistence;

namespace UrbanX.Order.Application.Behaviors;

internal sealed class OrderTransactionBehavior<TRequest, TResponse>(
    OrderDbContext dbContext,
    ILogger<OrderTransactionBehavior<TRequest, TResponse>> logger)
    : TransactionPipelineBehavior<TRequest, TResponse, OrderDbContext>(dbContext, logger)
    where TRequest : ICommandBase
    where TResponse : notnull;
