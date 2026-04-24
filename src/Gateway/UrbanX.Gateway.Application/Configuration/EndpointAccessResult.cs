namespace UrbanX.Gateway.Application.Configuration;

public sealed class EndpointAccessResult
{
    public static EndpointAccessResult ForPublic() =>
        new() { Kind = EndpointAccessKind.Public };

    public static EndpointAccessResult ForAuthenticated() =>
        new() { Kind = EndpointAccessKind.Authenticated };

    public static EndpointAccessResult ForPermission(
        string? ownPermission,
        string? allPermission,
        bool requiresMfa) =>
        new()
        {
            Kind = EndpointAccessKind.Permission,
            OwnPermission = ownPermission,
            AllPermission = allPermission,
            RequiresMfa = requiresMfa
        };

    public required EndpointAccessKind Kind { get; init; }
    public string? OwnPermission { get; init; }
    public string? AllPermission { get; init; }
    public bool RequiresMfa { get; init; }
}
