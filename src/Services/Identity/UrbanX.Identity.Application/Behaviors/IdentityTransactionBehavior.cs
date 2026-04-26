using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.Identity.Persistence;

namespace UrbanX.Identity.Application.Behaviors;

internal sealed class IdentityTransactionBehavior<TRequest, TResponse>(
    IdentityDbContext dbContext,
    ILogger<IdentityTransactionBehavior<TRequest, TResponse>> logger)
    : TransactionPipelineBehavior<TRequest, TResponse, IdentityDbContext>(dbContext, logger)
    where TRequest : ICommandBase
    where TResponse : notnull;
