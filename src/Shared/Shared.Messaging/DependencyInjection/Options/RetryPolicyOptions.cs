namespace Shared.Messaging.DependencyInjection.Options
{
    public sealed class RetryPolicyOptions
    {
        public int ImmediateCount { get; set; } = 3;
        public int DelayedCount { get; set; } = 5;
        public int DelaySeconds { get; set; } = 15;
    }
}
