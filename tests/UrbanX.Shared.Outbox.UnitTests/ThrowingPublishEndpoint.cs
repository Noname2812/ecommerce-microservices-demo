using MassTransit;

namespace UrbanX.Shared.Outbox.UnitTests;

/// <summary>
/// Minimal <see cref="IPublishEndpoint"/> whose publish operations fail (simulates broker down).
/// </summary>
internal sealed class ThrowingPublishEndpoint : IPublishEndpoint
{
    private readonly Exception _exception;

    public ThrowingPublishEndpoint(Exception exception)
    {
        _exception = exception;
    }

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
        throw new NotSupportedException();

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class =>
        Task.FromException(_exception);

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Publish(object message, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Publish(
        object message,
        Type messageType,
        IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Publish<T>(object message, CancellationToken cancellationToken = default) where T : class =>
        Task.FromException(_exception);

    public Task Publish<T>(object message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Publish<T>(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);
}
