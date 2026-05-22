using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleOrderCancelled;

[AllowAnonymous]
public sealed record HandleOrderCancelledCommand(Guid OrderId, string Reason) : ICommand;
