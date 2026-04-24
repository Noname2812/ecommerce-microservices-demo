namespace Shared.Outbox.DependencyInjection.Options
{
    /// <summary>
    /// Configuration options for the Outbox pattern.
    /// Bind from appsettings.json section "SharedKernel:Outbox".
    /// </summary>
    public sealed class OutboxOptions
    {
        public const string SectionName = "Shared:Outbox";

        public const int MaxRetries = 5;

        /// <summary>How many pending messages to fetch per polling cycle.</summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>Polling interval in seconds.</summary>
        public int PollingIntervalSeconds { get; set; } = 10;

        /// <summary>Maximum retry attempts before permanently failing a message.</summary>
        public int MaxRetryAttempts { get; set; } = MaxRetries;

        /// <summary>
        /// Enable dead-letter queue routing for permanently failed messages.
        /// Messages are published to a separate exchange: outbox.dead-letter
        /// </summary>
        public bool EnableDeadLetterQueue { get; set; } = true;
    }
}
