namespace Shared.Application.Authorization;

public interface IUserContext
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? MerchantId { get; }
    IReadOnlyCollection<string> Roles { get; }
    PermissionScope Scope { get; }
    string? RequestId { get; }
    bool HasRole(string role);
}
