using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Shared.Messaging
{

    /// <summary>
    /// MassTransit-backed request/response client.
    /// Wraps IRequestClient for typed service-to-service communication.
    /// </summary>
    internal sealed class MessageRequestClient : IMessageRequestClient
    {
        private readonly IBus _bus;
        private readonly ILogger<MessageRequestClient> _logger;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public MessageRequestClient(IBus bus, ILogger<MessageRequestClient> logger)
        {
            _bus = bus;
            _logger = logger;
        }

        public async Task<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            var effectiveTimeout = timeout ?? DefaultTimeout;
            var requestName = typeof(TRequest).Name;

            _logger.LogInformation("Sending request {RequestName} with timeout {Timeout}", requestName, effectiveTimeout);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            try
            {
                var client = _bus.CreateRequestClient<TRequest>(effectiveTimeout);
                var response = await client.GetResponse<TResponse>(request, cts.Token);

                _logger.LogInformation("Received response for {RequestName}", requestName);
                return response.Message;
            }
            catch (RequestTimeoutException ex)
            {
                _logger.LogError(ex, "Request {RequestName} timed out after {Timeout}", requestName, effectiveTimeout);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Request {RequestName} failed", requestName);
                throw;
            }
        }
    }

}
