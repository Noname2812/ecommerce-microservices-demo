namespace Shared.Outbox.DependencyInjection.Options
{
    /// <summary>
    /// Configuration options for the Outbox pattern.
    /// Bind from appsettings.json section "SharedKernel:Outbox".
    /// </summary>
    public sealed class OutboxOptions
    {
        public const string SectionName = "Shared:Outbox";

        private const int DefaultMaxRetryAttempts = 5;

        /// <summary>How many pending messages to fetch per polling cycle.</summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>Polling interval in seconds.</summary>
        public int PollingIntervalSeconds { get; set; } = 5;

        /// <summary>Maximum retry attempts before permanently failing a message.</summary>
        public int MaxRetryAttempts { get; set; } = DefaultMaxRetryAttempts;
    }
}
