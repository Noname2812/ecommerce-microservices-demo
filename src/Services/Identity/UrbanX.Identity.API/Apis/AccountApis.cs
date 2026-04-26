using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Command;

namespace UrbanX.Identity.API.Apis;

public class AccountApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/account";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BaseUrl).WithTags("Account");

        group.MapPost("/register", RegisterV1);
        group.MapPost("/confirm-email", ConfirmEmailV1);
        group.MapPost("/forgot-password", ForgotPasswordV1);
        group.MapPost("/reset-password", ResetPasswordV1);
    }

    public static async Task<IResult> RegisterV1(
        [FromServices] ISender sender,
        [FromBody] RegisterUserCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        if (result.IsFailure) return ToIdentityResult(result);
        var response = result.Value!;
        return Results.Created($"/api/v1/identity/users/{response.UserId}", response);
    }

    public static async Task<IResult> ConfirmEmailV1(
        [FromServices] ISender sender,
        [FromBody] ConfirmEmailCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public static async Task<IResult> ForgotPasswordV1(
        [FromServices] ISender sender,
        [FromBody] ForgotPasswordCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }

    public static async Task<IResult> ResetPasswordV1(
        [FromServices] ISender sender,
        [FromBody] ResetPasswordCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToIdentityResult(result);
    }
}
