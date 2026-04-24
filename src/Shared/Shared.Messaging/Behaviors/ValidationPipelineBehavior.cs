using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Primitives;

namespace Shared.Messaging.Behaviors
{

    /// <summary>
    /// MediatR pipeline behavior for FluentValidation integration.
    /// Validates all commands before they reach the handler.
    /// Register IValidator implementations and this will intercept automatically.
    /// </summary>
    public sealed class ValidationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;
        private readonly ILogger<ValidationPipelineBehavior<TRequest, TResponse>> _logger;

        public ValidationPipelineBehavior(
            IEnumerable<IValidator<TRequest>> validators,
            ILogger<ValidationPipelineBehavior<TRequest, TResponse>> logger)
        {
            _validators = validators;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (!_validators.Any())
                return await next();

            var requestName = typeof(TRequest).Name;

            var context = new ValidationContext<TRequest>(request);
            var validationResults = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = validationResults
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count == 0)
                return await next();

            _logger.LogWarning(
                "Validation failed for {RequestName} with {ErrorCount} error(s): {@Errors}",
                requestName, failures.Count, failures);

            // Return a Result.Failure if TResponse is a Result type; otherwise throw
            if (typeof(TResponse) == typeof(Result))
            {
                var error = new Error("Validation", string.Join("; ", failures.Select(f => f.ErrorMessage)));
                return (TResponse)(object)Result.Failure(error);
            }

            if (typeof(TResponse).IsGenericType &&
                typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
            {
                var innerType = typeof(TResponse).GetGenericArguments()[0];
                var error = new Error("Validation", string.Join("; ", failures.Select(f => f.ErrorMessage)));
                var failureMethod = typeof(Result<>)
                    .MakeGenericType(innerType)
                    .GetMethod(nameof(Result<object>.Failure), new[] { typeof(Error) })!;
                return (TResponse)failureMethod.Invoke(null, new object[] { error })!;
            }

            throw new ValidationException(failures);
        }
    }

}
