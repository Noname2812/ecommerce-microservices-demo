using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Messaging.Behaviors
{
    /// <summary>
    /// MediatR pipeline behavior that logs request/response with timing and structured metadata.
    /// Wraps every command and query handler automatically.
    /// </summary>
    public sealed class LoggingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly ILogger<LoggingPipelineBehavior<TRequest, TResponse>> _logger;

        public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
        {
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            var sw = Stopwatch.StartNew();

            _logger.LogInformation("Handling {RequestName} {@Request}", requestName, request);

            try
            {
                var response = await next();
                sw.Stop();

                _logger.LogInformation(
                    "Handled {RequestName} in {ElapsedMs}ms",
                    requestName, sw.ElapsedMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Error handling {RequestName} after {ElapsedMs}ms",
                    requestName, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
