using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Command;
using UrbanX.Identity.Application.Usecases.V1.Query;

namespace UrbanX.Identity.API.Apis;

public class UsersApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/identity/users";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Users")
            .MapGroup(BaseUrl).HasApiVersion(1);

        group.MapGet("/", ListUsersV1);
        group.MapGet("/{id:guid}", GetUserByIdV1);
        group.MapPost("/{id:guid}/roles", AssignRoleV1);
        group.MapDelete("/{id:guid}/roles/{role}", RevokeRoleV1);
        group.MapPost("/{id:guid}/deactivate", DeactivateV1);
        group.MapPost("/{id:guid}/activate", ActivateV1);
    }

    public static async Task<IResult> ListUsersV1(
        [FromServices] ISender sender,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new ListUsersQuery(pageIndex, pageSize, searchTerm, role, isActive),
            cancellationToken);
        return ToIdentityResult(result);
    }

    public static async Task<IResult> GetUserByIdV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetUserByIdQuery(id), cancellationToken);
        return ToIdentityResult(result);
    }

    public record AssignRoleRequest(string Role);

    public static async Task<IResult> AssignRoleV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] AssignRoleRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AssignRoleCommand(id, body.Role), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public static async Task<IResult> RevokeRoleV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromRoute] string role,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RevokeRoleCommand(id, role), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public record DeactivateRequest(string? Reason);

    public static async Task<IResult> DeactivateV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] DeactivateRequest? body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeactivateUserCommand(id, body?.Reason), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public static async Task<IResult> ActivateV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ActivateUserCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }
}
