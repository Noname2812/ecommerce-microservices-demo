namespace UrbanX.Order.Infrastructure.Services;

public sealed record ReserveRequest(
    string RequestIdempotencyKey,
    IReadOnlyList<ReserveLineItem> Items);

public sealed record ReserveLineItem(Guid ProductId, int Quantity);

public sealed record ReserveResponse(
    Guid ReservationId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ReservedItemResponse> Items);

public sealed record ReservedItemResponse(Guid ProductId, int Quantity);
