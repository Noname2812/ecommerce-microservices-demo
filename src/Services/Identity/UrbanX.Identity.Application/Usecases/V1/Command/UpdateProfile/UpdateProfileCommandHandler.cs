using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Identity;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class UpdateProfileCommandHandler : ICommandHandler<UpdateProfileCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserContext _userContext;
    private readonly IIdentityAuditWriter _audit;
    private readonly IEventPublisher _eventPublisher;

    public UpdateProfileCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserContext userContext,
        IIdentityAuditWriter audit,
        IEventPublisher eventPublisher)
    {
        _userManager = userManager;
        _userContext = userContext;
        _audit = audit;
        _eventPublisher = eventPublisher;
    }

    public async Task<Result> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is null)
            return Result.Failure(AuthErrors.InvalidCredentials);

        var userId = _userContext.UserId.Value;
        var user = await _userManager.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(userId));

        user.DisplayName = request.DisplayName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.AvatarUrl = request.AvatarUrl;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        user.Profile ??= new UserProfile { UserId = userId };
        user.Profile.Bio = request.Bio;
        user.Profile.DateOfBirth = request.DateOfBirth;
        user.Profile.Gender = request.Gender;
        user.Profile.AddressLine = request.AddressLine;
        user.Profile.City = request.City;
        user.Profile.Country = request.Country;
        user.Profile.PostalCode = request.PostalCode;
        user.Profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.ProfileUpdated, null, cancellationToken);
        await _eventPublisher.PublishAsync(new UserIntegrationEvents.UserProfileUpdatedV1(
            user.Id, user.Email!, user.DisplayName, user.PhoneNumber, user.AvatarUrl
        ), cancellationToken);

        return Result.Success();
    }
}
