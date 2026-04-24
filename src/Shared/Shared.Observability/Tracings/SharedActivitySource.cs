using System.Diagnostics;

namespace Shared.Observability.Tracings
{
    /// <summary>
    /// Centralised ActivitySource for SharedKernel instrumentation.
    /// Use this to create spans for messaging operations across all services.
    /// </summary>
    public static class SharedActivitySource
    {
        public const string SourceName = "SharedKernel.Messaging";
        public const string Version = "1.0.0";

        private static readonly ActivitySource Source = new(SourceName, Version);

        /// <summary>Start a span for publishing an integration event.</summary>
        public static Activity? StartPublish(string eventName, string? correlationId = null)
        {
            var activity = Source.StartActivity(
                $"publish {eventName}",
                ActivityKind.Producer);

            if (activity is null) return null;

            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.operation", "publish");
            activity.SetTag("messaging.destination", eventName);

            if (correlationId is not null)
                activity.SetTag("correlation_id", correlationId);

            return activity;
        }

        /// <summary>Start a span for consuming an integration event.</summary>
        public static Activity? StartConsume(string eventName, string? correlationId = null)
        {
            var activity = Source.StartActivity(
                $"consume {eventName}",
                ActivityKind.Consumer);

            if (activity is null) return null;

            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.operation", "receive");
            activity.SetTag("messaging.destination", eventName);

            if (correlationId is not null)
                activity.SetTag("correlation_id", correlationId);

            return activity;
        }

        /// <summary>Start a span for a MediatR command or query.</summary>
        public static Activity? StartMediator(string requestName)
        {
            var activity = Source.StartActivity(
                $"mediator {requestName}",
                ActivityKind.Internal);

            activity?.SetTag("mediator.request", requestName);
            return activity;
        }

        /// <summary>Mark the current activity as failed with the exception details.</summary>
        public static void RecordException(Activity? activity, Exception ex)
        {
            if (activity is null) return;
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);
        }
    }

}
