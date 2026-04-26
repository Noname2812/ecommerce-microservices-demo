namespace UrbanX.Identity.Application.Usecases.V1.Query;

public record UserSummaryDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    string? AvatarUrl,
    Guid? MerchantId,
    bool IsActive,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles
);

public record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    string? AvatarUrl,
    Guid? MerchantId,
    bool IsActive,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string? Bio,
    DateOnly? DateOfBirth,
    string? Gender,
    string? AddressLine,
    string? City,
    string? Country,
    string? PostalCode
);

public record RoleDto(Guid Id, string Name, string? Description);

public record AuthAuditLogDto(
    Guid Id,
    Guid? UserId,
    string? Email,
    string EventType,
    string? IpAddress,
    string? UserAgent,
    string? Metadata,
    DateTimeOffset OccurredAt
);
