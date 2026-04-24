using System.Text.Json.Serialization;

namespace UrbanX.Catalog.Domain.ValueObjects
{
    /// <summary>JSON shape stored in products.dimensions (jsonb): length, width, height in centimeters.</summary>
    public sealed record ProductDimensions
    {
        [JsonPropertyName("length_cm")]
        public decimal? LengthCm { get; init; }

        [JsonPropertyName("width_cm")]
        public decimal? WidthCm { get; init; }

        [JsonPropertyName("height_cm")]
        public decimal? HeightCm { get; init; }
    }
}
