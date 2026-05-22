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
    }
}
