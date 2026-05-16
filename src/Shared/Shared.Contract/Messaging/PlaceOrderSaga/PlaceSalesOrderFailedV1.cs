using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record PlaceSalesOrderFailedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required string FailureStep { get; init; }
    public required string FailureReason { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}
