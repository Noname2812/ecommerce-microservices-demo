using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

public sealed class CatalogServiceClientResilienceTests
{
    [Fact]
    public async Task CircuitBreaker_AfterFailures_OpensAndFailFastWithoutHttp()
    {
        var httpCalls = 0;
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new CatalogClientResilienceOptions
        {
            CbMinimumThroughput = 3,
            CbFailureRatio = 0.5,
            CbBreakDurationSeconds = 30,
            CbSamplingDurationSeconds = 30,
            RetryMaxAttempts = 1,
            AttemptTimeoutSeconds = 1,
            TotalTimeoutSeconds = 2
        }));
        services
            .AddHttpClient<ICatalogServiceClient, CatalogServiceClient>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://catalog.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new CountingHandler(() =>
            {
                Interlocked.Increment(ref httpCalls);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }))
            .AddStandardResilienceHandler()
            .Configure((options, sp) =>
            {
                var resilience = sp.GetRequiredService<IOptions<CatalogClientResilienceOptions>>().Value;
                ServiceCollectionExtensions.ApplyCatalogClientResilience(options, resilience);
            });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICatalogServiceClient>();
        var variantId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            var result = await client.GetVariantsAsync([variantId], CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
        }

        var callsBeforeCircuit = httpCalls;
        Assert.True(callsBeforeCircuit >= 3);

        var failFast = await client.GetVariantsAsync([variantId], CancellationToken.None);
        Assert.True(failFast.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, failFast.Error.Code);
        Assert.Equal(callsBeforeCircuit, httpCalls);
    }

    private sealed class CountingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
