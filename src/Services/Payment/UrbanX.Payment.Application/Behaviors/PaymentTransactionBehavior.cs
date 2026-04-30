using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.Payment.Persistence;

namespace UrbanX.Payment.Application.Behaviors;

internal sealed class PaymentTransactionBehavior<TRequest, TResponse>(
    PaymentDbContext dbContext,
    ILogger<PaymentTransactionBehavior<TRequest, TResponse>> logger)
    : TransactionPipelineBehavior<TRequest, TResponse, PaymentDbContext>(dbContext, logger)
    where TRequest : ICommandBase
    where TResponse : notnull;
