using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Command;
using UrbanX.Identity.Application.Usecases.V1.Query;

namespace UrbanX.Identity.API.Apis;

public class ProfileApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/identity";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Profile")
            .MapGroup(BaseUrl).HasApiVersion(1);

        group.MapGet("/me", GetCurrentUserV1);
        group.MapGet("/profile", GetProfileV1);
        group.MapPut("/profile", UpdateProfileV1);
        group.MapPost("/change-password", ChangePasswordV1);
    }

    public static async Task<IResult> GetCurrentUserV1(
        [FromServices] ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return ToIdentityResult(result);
    }

    public static async Task<IResult> GetProfileV1(
        [FromServices] ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return ToIdentityResult(result);
    }

    public static async Task<IResult> UpdateProfileV1(
        [FromServices] ISender sender,
        [FromBody] UpdateProfileCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public static async Task<IResult> ChangePasswordV1(
        [FromServices] ISender sender,
        [FromBody] ChangePasswordCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }
}
