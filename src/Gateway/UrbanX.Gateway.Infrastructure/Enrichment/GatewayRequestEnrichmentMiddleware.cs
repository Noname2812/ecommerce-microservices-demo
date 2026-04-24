using Microsoft.AspNetCore.Http;
using UrbanX.Gateway.Application.Abstractions;

namespace UrbanX.Gateway.Infrastructure.Enrichment;

public sealed class GatewayRequestEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestHeaderEnricher _enricher;

    public GatewayRequestEnrichmentMiddleware(RequestDelegate next, IRequestHeaderEnricher enricher)
    {
        _next = next;
        _enricher = enricher;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _enricher.Apply(context);
        await _next(context).ConfigureAwait(false);
    }
}
