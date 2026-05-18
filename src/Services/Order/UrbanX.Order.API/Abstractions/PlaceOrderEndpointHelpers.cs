using Shared.Application.Authorization;

namespace UrbanX.Order.API.Abstractions;

internal static class PlaceOrderEndpointHelpers
{
    public static IResult? RequireUserId(IUserContext userContext)
    {
        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Results.Problem(
                detail: "Authenticated user was not found in request context.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "AUTH_REQUIRED");
        return null;
    }

    public static IResult Accepted202(Guid ticketId, string locationUri)
        => Results.Accepted(
            uri: locationUri,
            value: new { ticketId });
}
