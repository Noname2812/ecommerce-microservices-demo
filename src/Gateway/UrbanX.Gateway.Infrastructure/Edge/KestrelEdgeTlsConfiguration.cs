using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Security.Edge;
using UrbanX.Gateway.Application.Abstractions;

namespace UrbanX.Gateway.Infrastructure.Edge;

/// <summary>
/// Optional in-process HTTPS for edge TLS. Use when the process terminates TLS (e.g. bare VM); otherwise
/// use an external load balancer and keep HTTP to downstream clusters.
/// </summary>
public sealed class KestrelEdgeTlsConfiguration : IKestrelEdgeTlsConfiguration
{
    public void Apply(KestrelServerOptions kestrel, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(KestrelEdgeOptions.SectionName);
        var o = section.Get<KestrelEdgeOptions>();
        if (o is not { HttpsEnabled: true } || string.IsNullOrWhiteSpace(o.PfxPath) || o.HttpsPort < 1)
        {
            return;
        }

        var pfx = o.PfxPath!;

        if (!File.Exists(pfx))
        {
            // No-op when the certificate is missing (e.g. local dev using HTTP only).
            return;
        }

        if (o.ListenAddress is not ("*" or "0.0.0.0" or "Any" or null)
            && !IPAddress.TryParse(o.ListenAddress, out var _))
        {
            return;
        }

        // PFX / P12 with optional password. Ephemeral key set avoids file-permission issues on some hosts.
        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12FromFile(
                pfx,
                o.PfxPassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch
        {
            return;
        }

        if (o.ListenAddress is "*" or "0.0.0.0" or "Any" or null)
        {
            kestrel.ListenAnyIP(o.HttpsPort, l => l.UseHttps(cert));
            return;
        }

        var ip = IPAddress.Parse(o.ListenAddress!);
        kestrel.Listen(new IPEndPoint(ip, o.HttpsPort), l => l.UseHttps(cert));
    }
}
