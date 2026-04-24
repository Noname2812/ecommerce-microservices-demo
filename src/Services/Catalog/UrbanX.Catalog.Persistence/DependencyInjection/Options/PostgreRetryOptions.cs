using System.ComponentModel.DataAnnotations;

namespace UrbanX.Catalog.Persistence.DependencyInjection.Options
{
    public class PostgreRetryOptions
    {
        [Required, Range(5, 20)] public int MaxRetryCount { get; init; }
        [Required, Timestamp] public TimeSpan MaxRetryDelay { get; init; }
        public int[]? ErrorNumbersToAdd { get; init; }
    }
}
