using System.Text.Json;

namespace Shared.Outbox
{
    /// <summary>Shared JSON options for <see cref="CompensationOutboxWriter"/> and <see cref="CompensationOutboxRelayWorker"/> (camelCase).</summary>
    internal static class CompensationOutboxJsonSerializerOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
