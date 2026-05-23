namespace UrbanX.Order.Infrastructure.Sagas.PlaceOrderNormal;

/// <summary>
/// Per-step timeout for <see cref="PlaceOrderNormalSagaStateMachine"/> (single schedule per saga).
/// Correlated by <see cref="OrderId"/>.
/// </summary>
public abstract record OrderSagaStepTimeoutV1
{
    public Guid OrderId { get; init; }
}

/// <summary>
/// Place-normal saga schedules must use distinct message types (MassTransit schedule registry key).
/// </summary>
public record InventoryStepTimeoutV1 : OrderSagaStepTimeoutV1
{
}

public record CouponStepTimeoutV1 : OrderSagaStepTimeoutV1
{
}

public record PaymentSessionStepTimeoutV1 : OrderSagaStepTimeoutV1
{
}
