using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Observability.DependencyInjection.Options;
using Shared.Observability.Metrics;
using Shared.Observability.Tracings;


namespace Shared.Observability.DependencyInjection.Extensions
{
    public static class ObservabilityServiceCollectionExtensions
    {
        /// <summary>
        /// Registers OpenTelemetry tracing + metrics with OTLP exporter.
        ///
        /// Example:
        /// <code>
        /// builder.AddObservability(builder.Configuration);
        /// </code>
        ///
        /// appsettings.json:
        /// <code>
        /// "Shared": {
        ///   "Tracing": {
        ///     "ServiceName": "order-service",
        ///     "ServiceVersion": "2.1.0",
        ///     "Environment": "production",
        ///     "OtlpEndpoint": "http://otel-collector:4317",
        ///     "SamplingRatio": 0.1
        ///   }
        /// }
        /// </code>
        /// </summary>
        public static IServiceCollection AddObservability(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {

            var options = new TracingOptions();
            configuration?.GetSection(TracingOptions.SectionName).Bind(options);

            // ── Metrics ───────────────────────────────────────────────────────
            services.AddSingleton<SharedMetrics>();

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: options.ServiceName,
                        serviceVersion: options.ServiceVersion)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = options.Environment
                    }))
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddMeter(SharedMetrics.MeterName)
                        .AddMeter("MassTransit");

                    if (options.OtlpEndpoint is not null)
                        metrics.AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint));

                    if (options.ConsoleExporter)
                        metrics.AddConsoleExporter();
                })
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation(o =>
                        {
                            o.RecordException = true;
                            o.Filter = ctx =>
                                !ctx.Request.Path.StartsWithSegments("/health") &&
                                !ctx.Request.Path.StartsWithSegments("/metrics");
                        })
                        .AddHttpClientInstrumentation(o => o.RecordException = true)
                        .AddSource(SharedActivitySource.SourceName)
                        .AddSource("MassTransit")
                        .SetSampler(new TraceIdRatioBasedSampler(options.SamplingRatio));

                    if (options.OtlpEndpoint is not null)
                        tracing.AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint));

                    if (options.ConsoleExporter)
                        tracing.AddConsoleExporter();
                });

            return services;
        }
    }

}
