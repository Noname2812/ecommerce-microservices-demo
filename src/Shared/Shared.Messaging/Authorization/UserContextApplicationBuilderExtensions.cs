using Microsoft.AspNetCore.Builder;

namespace Shared.Messaging.Authorization;

public static class UserContextApplicationBuilderExtensions
{
    public static IApplicationBuilder UseUserContext(this IApplicationBuilder app) =>
        app.UseMiddleware<UserContextMiddleware>();
}
