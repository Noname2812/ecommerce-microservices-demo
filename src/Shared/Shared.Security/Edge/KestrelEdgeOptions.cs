using System.ComponentModel.DataAnnotations;

namespace Shared.Security.Edge;

/// <summary>
/// Optional HTTPS listener for edge TLS termination (Kestrel). Omitted in development; used in
/// production when terminating TLS on the gateway process.
/// </summary>
public sealed class KestrelEdgeOptions
{
    public const string SectionName = "Kestrel:Edge";

    /// <summary>When true, a dedicated HTTPS endpoint is added using <see cref="PfxPath"/>.</summary>
    public bool HttpsEnabled { get; set; }

    /// <summary>IP address to listen on, e.g. 0.0.0.0. Empty means Any.</summary>
    public string? ListenAddress { get; set; }

    [Range(1, 65_535)]
    public int HttpsPort { get; set; } = 8443;

    /// <summary>Path to a PFX (or P12) file containing the server certificate.</summary>
    public string? PfxPath { get; set; }

    public string? PfxPassword { get; set; }
}
