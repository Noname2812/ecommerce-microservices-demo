using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Query;

namespace UrbanX.Identity.API.Apis;

public class AuditApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/identity/audit-logs";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("AuditLogs")
            .MapGroup(BaseUrl).HasApiVersion(1);

        group.MapGet("/", ListAuditLogsV1);
    }

    public static async Task<IResult> ListAuditLogsV1(
        [FromServices] ISender sender,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new ListAuditLogsQuery(userId, eventType, from, to, pageIndex, pageSize),
            cancellationToken);
        return ToIdentityResult(result);
    }
}
