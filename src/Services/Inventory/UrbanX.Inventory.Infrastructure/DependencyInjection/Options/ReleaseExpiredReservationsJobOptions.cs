using System.ComponentModel.DataAnnotations;

namespace UrbanX.Inventory.Infrastructure.DependencyInjection.Options;

public sealed class ReleaseExpiredReservationsJobOptions
{
    public const string SectionName = "Inventory:Jobs:ReleaseExpiredReservations";

    /// <summary>Max reservations processed per run. Default: 200.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 200;

    /// <summary>Hangfire cron expression. Default: every 2 minutes.</summary>
    [Required]
    public string CronExpression { get; set; } = "*/2 * * * *";
}
