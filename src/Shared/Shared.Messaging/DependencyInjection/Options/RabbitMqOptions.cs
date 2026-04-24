namespace Shared.Messaging.DependencyInjection.Options
{
    /// <summary>
    /// RabbitMQ connection options.
    /// Bind from appsettings.json section "Shared:RabbitMQ".
    /// </summary>
    public sealed class RabbitMqOptions
    {
        public const string SectionName = "Shared:RabbitMQ";

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string VirtualHost { get; set; } = "/";
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";

        /// <summary>
        /// Enables the RabbitMQ publisher confirms feature.
        /// Guarantees that messages were accepted by the broker before returning.
        /// Recommended for production.
        /// </summary>
        public bool PublisherConfirms { get; set; } = true;

        /// <summary>
        /// Prefetch count per consumer. Lower = less memory pressure under load.
        /// </summary>
        public ushort PrefetchCount { get; set; } = 16;

        /// <summary>Number of concurrent messages processed per consumer endpoint.</summary>
        public int ConcurrentMessageLimit { get; set; } = 8;

        /// <summary>
        /// Retry policy for failed consumers.
        /// Retries: ImmediateCount times immediately, then DelayedCount times with DelaySeconds delay.
        /// </summary>
        public RetryPolicyOptions Retry { get; set; } = new();
    }
}
