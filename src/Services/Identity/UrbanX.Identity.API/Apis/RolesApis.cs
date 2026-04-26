using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Query;

namespace UrbanX.Identity.API.Apis;

public class RolesApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/identity/roles";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Roles")
            .MapGroup(BaseUrl).HasApiVersion(1);

        group.MapGet("/", ListAllRolesV1);
    }

    public static async Task<IResult> ListAllRolesV1(
        [FromServices] ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListAllRolesQuery(), cancellationToken);
        return ToIdentityResult(result);
    }
}
