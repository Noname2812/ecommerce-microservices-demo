using System.Diagnostics.Metrics;

namespace Shared.Observability.Metrics;

/// <summary>
/// Centralised metrics for SharedKernel messaging operations.
/// Exposes counters and histograms compatible with Prometheus / OpenTelemetry.
/// </summary>
public sealed class SharedMetrics : IDisposable
{
    public const string MeterName = "Shared.Messaging";

    private readonly Meter _meter;

    // ── Counters ─────────────────────────────────────────────────────────
    public readonly Counter<long> EventsPublished;
    public readonly Counter<long> EventsConsumed;
    public readonly Counter<long> EventsConsumedFailed;
    public readonly Counter<long> OutboxMessagesWritten;
    public readonly Counter<long> OutboxMessagesRelayed;
    public readonly Counter<long> OutboxMessagesFailed;
    public readonly Counter<long> CommandsDispatched;
    public readonly Counter<long> CommandsFailed;

    // ── Histograms ────────────────────────────────────────────────────────
    public readonly Histogram<double> CommandDurationMs;
    public readonly Histogram<double> OutboxRelayDurationMs;

    public SharedMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, "1.0.0");

        EventsPublished = _meter.CreateCounter<long>(
            "sharedkernel.events.published",
            unit: "{events}",
            description: "Number of integration events published to the message bus");

        EventsConsumed = _meter.CreateCounter<long>(
            "sharedkernel.events.consumed",
            unit: "{events}",
            description: "Number of integration events successfully consumed");

        EventsConsumedFailed = _meter.CreateCounter<long>(
            "sharedkernel.events.consumed.failed",
            unit: "{events}",
            description: "Number of integration events that failed during consumption");

        OutboxMessagesWritten = _meter.CreateCounter<long>(
            "sharedkernel.outbox.written",
            unit: "{messages}",
            description: "Number of messages written to the outbox table");

        OutboxMessagesRelayed = _meter.CreateCounter<long>(
            "sharedkernel.outbox.relayed",
            unit: "{messages}",
            description: "Number of outbox messages successfully relayed to the bus");

        OutboxMessagesFailed = _meter.CreateCounter<long>(
            "sharedkernel.outbox.failed",
            unit: "{messages}",
            description: "Number of outbox messages that permanently failed");

        CommandsDispatched = _meter.CreateCounter<long>(
            "sharedkernel.commands.dispatched",
            unit: "{commands}",
            description: "Number of MediatR commands dispatched");

        CommandsFailed = _meter.CreateCounter<long>(
            "sharedkernel.commands.failed",
            unit: "{commands}",
            description: "Number of MediatR commands that resulted in failure");

        CommandDurationMs = _meter.CreateHistogram<double>(
            "sharedkernel.commands.duration",
            unit: "ms",
            description: "Duration of MediatR command handling in milliseconds");

        OutboxRelayDurationMs = _meter.CreateHistogram<double>(
            "sharedkernel.outbox.relay.duration",
            unit: "ms",
            description: "Duration of outbox relay cycles in milliseconds");
    }

    public void Dispose() => _meter.Dispose();
}
