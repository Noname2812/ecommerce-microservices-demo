using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

// Requires Catalog API at http://localhost:5290 with seeded data.
[Trait("Category", "Integration")]
public sealed class CatalogServiceClientIntegrationTests : IAsyncLifetime
{
    private const string CatalogBaseAddress = "http://localhost:5290";

    private ICatalogServiceClient? _client;
    private ServiceProvider? _provider;
    private bool _catalogAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            using var probe = new HttpClient { BaseAddress = new Uri(CatalogBaseAddress), Timeout = TimeSpan.FromSeconds(2) };
            using var response = await probe.GetAsync("/health", CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                return;

            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new CatalogClientOptions
            {
                BaseAddress = CatalogBaseAddress
            }));
            services.AddSingleton(Options.Create(new CatalogClientResilienceOptions()));
            services
                .AddHttpClient<ICatalogServiceClient, CatalogServiceClient>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(CatalogBaseAddress);
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .AddStandardResilienceHandler()
                .Configure((options, sp) =>
                {
                    var resilience = sp.GetRequiredService<IOptions<CatalogClientResilienceOptions>>().Value;
                    ServiceCollectionExtensions.ApplyResilience(
                        options, resilience, NullLogger<CatalogServiceClient>.Instance);
                });
            services.AddSingleton(NullLogger<CatalogServiceClient>.Instance);

            _provider = services.BuildServiceProvider();
            _client = _provider.GetRequiredService<ICatalogServiceClient>();
            _catalogAvailable = true;
        }
        catch
        {
            _catalogAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private void RequireCatalog() =>
        Skip.IfNot(_catalogAvailable && _client is not null, $"Catalog not available at {CatalogBaseAddress}.");

    [SkippableFact]
    public async Task GetVariantsAsync_KnownVariant_ReturnsData()
    {
        RequireCatalog();

        var variantId = await ResolveAnyVariantIdAsync();
        Skip.If(variantId is null, "No variants in catalog seed data.");

        var result = await _client!.GetVariantsAsync([variantId.Value], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(variantId, result.Value[0].VariantId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value[0].Sku));
    }

    [SkippableFact]
    public async Task GetVariantsAsync_UnknownVariant_ReturnsVariantNotFound()
    {
        RequireCatalog();

        var result = await _client!.GetVariantsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VARIANT_NOT_FOUND", result.Error.Message);
    }

    [SkippableFact]
    public async Task GetVariantsAsync_MultipleKnownVariants_ReturnsAll()
    {
        RequireCatalog();

        var ids = await ResolveVariantIdsAsync(5);
        Skip.If(ids.Count < 2, "Need at least 2 variants in catalog seed data.");

        var result = await _client!.GetVariantsAsync(ids, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ids.Count, result.Value.Count);
        Assert.All(ids, id => Assert.Contains(result.Value, v => v.VariantId == id));
    }

    private async Task<Guid?> ResolveAnyVariantIdAsync()
    {
        var ids = await ResolveVariantIdsAsync(1);
        return ids.Count > 0 ? ids[0] : null;
    }

    private async Task<List<Guid>> ResolveVariantIdsAsync(int take)
    {
        using var http = new HttpClient { BaseAddress = new Uri(CatalogBaseAddress) };
        using var response = await http.GetAsync("/api/v1/catalog/products?pageSize=5");
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("items", out var items))
            return [];

        var variantIds = new List<Guid>();
        foreach (var product in items.EnumerateArray())
        {
            if (!product.TryGetProperty("variants", out var variants))
                continue;

            foreach (var variant in variants.EnumerateArray())
            {
                if (variant.TryGetProperty("id", out var idEl) && Guid.TryParse(idEl.GetString(), out var id))
                    variantIds.Add(id);

                if (variantIds.Count >= take)
                    return variantIds;
            }
        }

        return variantIds;
    }
}
