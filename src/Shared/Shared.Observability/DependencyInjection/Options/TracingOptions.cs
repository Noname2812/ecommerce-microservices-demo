namespace Shared.Observability.DependencyInjection.Options
{
    /// <summary>
    /// Configuration for OpenTelemetry tracing.
    /// Bind from appsettings.json section "SharedKernel:Tracing".
    /// </summary>
    public sealed class TracingOptions
    {
        public const string SectionName = "Shared:Tracing";

        /// <summary>Service name reported to the tracing backend (e.g. Jaeger / Tempo).</summary>
        public string ServiceName { get; set; } = "unknown-service";

        /// <summary>Service version — usually set to the assembly version.</summary>
        public string ServiceVersion { get; set; } = "1.0.0";

        /// <summary>Deployment environment: development | staging | production.</summary>
        public string Environment { get; set; } = "development";

        /// <summary>OTLP gRPC endpoint, e.g. http://otel-collector:4317</summary>
        public string? OtlpEndpoint { get; set; }

        /// <summary>Sampling ratio 0.0–1.0. 1.0 = 100% (recommended for dev); lower for prod.</summary>
        public double SamplingRatio { get; set; } = 1.0;

        /// <summary>Log trace/span to console. Enable in development only.</summary>
        public bool ConsoleExporter { get; set; } = false;
    }
}
