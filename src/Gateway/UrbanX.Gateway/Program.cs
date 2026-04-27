using UrbanX.Gateway.Infrastructure.DependencyInjection;
using UrbanX.Gateway.Infrastructure.Edge;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Optional in-process Kestrel TLS. Skipped when <c>Kestrel:Edge:HttpsEnabled</c> is false (typical: TLS at a load balancer).
builder.WebHost.ConfigureKestrel((context, o) =>
{
    new KestrelEdgeTlsConfiguration()
        .Apply(o, context.Configuration, context.HostingEnvironment);
});

builder.Services.AddGatewayInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

// See docs/request-flow.md — CORS, then: correlation, rate limit, JWT, RBAC, header enrichment, structured logging.
app.UseGatewayEdgeCors();
app.UseGatewayDownstreamPipeline();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGatewayBff();
app.MapGatewayReverseProxy();

app.Run();

public partial class Program { }
