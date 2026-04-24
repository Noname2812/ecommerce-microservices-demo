using MassTransit;
using Microsoft.Extensions.Logging;

namespace Shared.Messaging.Filters
{

    /// <summary>
    /// MassTransit consume filter that extracts correlation/causation IDs from
    /// incoming message headers and populates the current activity's baggage
    /// for distributed tracing propagation.
    /// </summary>
    public sealed class CorrelationConsumeFilter<T> : IFilter<ConsumeContext<T>>
        where T : class
    {
        private readonly ILogger<CorrelationConsumeFilter<T>> _logger;

        public CorrelationConsumeFilter(ILogger<CorrelationConsumeFilter<T>> logger)
        {
            _logger = logger;
        }

        public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
        {
            var correlationId = context.CorrelationId?.ToString()
                ?? context.Headers.Get<string>("x-correlation-id")
                ?? Guid.NewGuid().ToString();

            var causationId = context.Headers.Get<string>("x-causation-id");
            var source = context.Headers.Get<string>("x-source") ?? "unknown";

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["CausationId"] = causationId ?? string.Empty,
                ["MessageSource"] = source,
                ["MessageType"] = typeof(T).Name
            });

            _logger.LogDebug(
                "Consuming message {MessageType} from {Source} [CorrelationId={CorrelationId}]",
                typeof(T).Name, source, correlationId);

            await next.Send(context);
        }

        public void Probe(ProbeContext context) =>
            context.CreateFilterScope("correlationConsumeFilter");
    }

}
