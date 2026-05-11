using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;

[AllowAnonymous]
public record RefreshProductProjectionCommand(Guid ProductId) : ICommand;
