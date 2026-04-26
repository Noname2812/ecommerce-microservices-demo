using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Identity
{
    public static class UserIntegrationEvents
    {
        public record UserRegisteredV1(
            Guid UserId,
            string Email,
            string DisplayName,
            string? PhoneNumber,
            Guid? MerchantId,
            IReadOnlyList<string> Roles
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }

        public record UserProfileUpdatedV1(
            Guid UserId,
            string Email,
            string DisplayName,
            string? PhoneNumber,
            string? AvatarUrl
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }

        public record UserRoleAssignedV1(
            Guid UserId,
            string Email,
            string Role,
            Guid? AssignedBy
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }

        public record UserRoleRevokedV1(
            Guid UserId,
            string Email,
            string Role,
            Guid? RevokedBy
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }

        public record UserDeactivatedV1(
            Guid UserId,
            string Email,
            Guid? DeactivatedBy,
            string? Reason
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }

        public record UserActivatedV1(
            Guid UserId,
            string Email,
            Guid? ActivatedBy
        ) : IntegrationEventBase
        {
            public override string Source => "identity-service";
        }
    }
}
