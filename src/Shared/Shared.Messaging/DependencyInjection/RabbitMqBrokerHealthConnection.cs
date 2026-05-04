using RabbitMQ.Client;

namespace Shared.Messaging.DependencyInjection;

/// <summary>
/// Single long-lived AMQP connection used only by the ASP.NET health check (see RabbitMQ client guidance).
/// Disposed with the host when the singleton is torn down.
/// </summary>
internal sealed class RabbitMqBrokerHealthConnection : IDisposable
{
    public RabbitMqBrokerHealthConnection(string amqpUri)
    {
        var factory = new ConnectionFactory { Uri = new Uri(amqpUri) };
        Connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
    }

    public IConnection Connection { get; }

    public void Dispose() => Connection.Dispose();
}
