using System.Diagnostics.Metrics;
using UrbanX.Order.Application.Constants;

namespace UrbanX.Order.Application.Telemetry;

/// <summary>
/// Metrics for tiered Catalog lookup used by Place Order validators.
/// Wire by adding <c>metrics.AddMeter(CatalogProjectionConstants.Metrics.MeterName)</c> in the OpenTelemetry meter provider.
/// </summary>
public static class OrderValidatorMetrics
{
    private static readonly Meter Meter = new(
        CatalogProjectionConstants.Metrics.MeterName,
        CatalogProjectionConstants.Metrics.MeterVersion);

    /// <summary>
    /// Counts validator lookups by resolved tier. Tag <c>validator</c> ∈ ValidatorNames; tag <c>source</c> ∈ Sources.
    /// </summary>
    public static readonly Counter<long> ValidatorSource =
        Meter.CreateCounter<long>(CatalogProjectionConstants.Metrics.ValidatorSourceCounter);

    /// <summary>
    /// Per-call duration of validator lookups. Tag <c>validator</c> ∈ ValidatorNames.
    /// </summary>
    public static readonly Histogram<double> ValidatorDuration =
        Meter.CreateHistogram<double>(CatalogProjectionConstants.Metrics.ValidatorDurationHistogram);
}
