using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Identity;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class AssignRoleCommandHandler : ICommandHandler<AssignRoleCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IUserContext _userContext;
    private readonly IIdentityAuditWriter _audit;
    private readonly IEventPublisher _eventPublisher;

    public AssignRoleCommandHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IUserContext userContext,
        IIdentityAuditWriter audit,
        IEventPublisher eventPublisher)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userContext = userContext;
        _audit = audit;
        _eventPublisher = eventPublisher;
    }

    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        if (_userContext.UserId == request.UserId)
            return Result.Failure(AuthErrors.CannotChangeOwnRole);

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(request.UserId));

        if (!await _roleManager.RoleExistsAsync(request.Role))
            return Result.Failure(AuthErrors.RoleNotFound(request.Role));

        if (await _userManager.IsInRoleAsync(user, request.Role))
            return Result.Failure(AuthErrors.UserAlreadyInRole(request.Role));

        var result = await _userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
            return Result.Failure(new Error("Role.AssignFailed", result.Errors.FirstOrDefault()?.Description ?? "Failed to assign role"));

        await _audit.WriteAsync(
            user.Id,
            user.Email,
            AuthEventType.RoleAssigned,
            new { role = request.Role, by = _userContext.UserId },
            cancellationToken);

        await _eventPublisher.PublishAsync(new UserIntegrationEvents.UserRoleAssignedV1(
            user.Id, user.Email!, request.Role, _userContext.UserId
        ), cancellationToken);

        return Result.Success();
    }
}
