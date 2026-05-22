using Microsoft.Extensions.Configuration;
using Shared.Messaging.DependencyInjection.Options;

namespace Shared.Messaging.DependencyInjection;

/// <summary>
/// Resolves the AMQP URI for health checks using the same rules as MassTransit RabbitMQ transport.
/// </summary>
internal static class RabbitMqConnectionResolver
{
    public static string? ResolveAmqpUri(IConfiguration configuration)
    {
        var aspireConnectionString = configuration.GetConnectionString("messaging");
        if (!string.IsNullOrWhiteSpace(aspireConnectionString))
            return aspireConnectionString.Trim();

        var section = configuration.GetSection(RabbitMqOptions.SectionName);
        if (!section.Exists())
            return null;

        var opt = section.Get<RabbitMqOptions>();
        if (opt is null || string.IsNullOrWhiteSpace(opt.Host))
            return null;

        var port = opt.Port > 0 ? opt.Port : 5672;

        var vhost = string.IsNullOrEmpty(opt.VirtualHost) || opt.VirtualHost == "/"
            ? "/"
            : opt.VirtualHost.TrimStart('/');

        var vhostInUri = vhost == "/"
            ? "/%2F"
            : $"/{Uri.EscapeDataString(vhost)}";

        var user = Uri.EscapeDataString(opt.Username);
        var pass = Uri.EscapeDataString(opt.Password);

        return $"amqp://{user}:{pass}@{opt.Host}:{port}{vhostInUri}";
    }
}
