using Shared.Application;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public record GetCurrentUserQuery() : IQuery<UserProfileDto>;
