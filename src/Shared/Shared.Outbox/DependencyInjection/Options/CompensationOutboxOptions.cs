namespace Shared.Outbox.DependencyInjection.Options
{
    /// <summary>
    /// Polling and retry settings for <see cref="CompensationOutboxRelayWorker"/>.
    /// Bind from configuration section <see cref="SectionName"/>.
    /// </summary>
    public sealed class CompensationOutboxOptions
    {
        public const string SectionName = "Shared:CompensationOutbox";

        private const int DefaultMaxRetryAttempts = 5;

        public int BatchSize { get; set; } = 50;

        /// <summary>Default 10 seconds (separate from main outbox relay).</summary>
        public int PollingIntervalSeconds { get; set; } = 10;

        public int MaxRetryAttempts { get; set; } = DefaultMaxRetryAttempts;

        /// <summary>RabbitMQ exchange for compensation integration events (not <c>order.events</c>).</summary>
        public string CompensationEventsExchange { get; set; } = "compensation.events";
    }
}
