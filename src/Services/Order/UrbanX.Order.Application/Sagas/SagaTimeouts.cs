namespace UrbanX.Order.Application.Sagas;

/// <summary>
/// Generic per-step timeout signal scheduled by the place-order sagas (Normal + Sales) to bound
/// the wait for each external response (catalog, inventory, payment session, etc.). Correlated
/// by <see cref="OrderId"/>.
/// </summary>
public record SagaStepTimeoutV1
{
    public Guid OrderId { get; init; }
}
