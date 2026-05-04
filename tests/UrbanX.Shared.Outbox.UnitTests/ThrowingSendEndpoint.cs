using MassTransit;

namespace UrbanX.Shared.Outbox.UnitTests;

/// <summary>
/// Minimal <see cref="ISendEndpoint"/> whose send operations fail (simulates broker down).
/// </summary>
internal sealed class ThrowingSendEndpoint : ISendEndpoint
{
    private readonly Exception _exception;

    public ThrowingSendEndpoint(Exception exception)
    {
        _exception = exception;
    }

    public ConnectHandle ConnectSendObserver(ISendObserver observer) =>
        throw new NotSupportedException();

    public Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class =>
        Task.FromException(_exception);

    public Task Send<T>(T message, IPipe<SendContext<T>> pipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Send<T>(T message, IPipe<SendContext> pipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Send(object message, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Send(object message, IPipe<SendContext> pipe, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Send(object message, Type messageType, CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Send(object message, Type messageType, IPipe<SendContext> pipe,
        CancellationToken cancellationToken = default) =>
        Task.FromException(_exception);

    public Task Send<T>(object message, CancellationToken cancellationToken = default) where T : class =>
        Task.FromException(_exception);

    public Task Send<T>(object message, IPipe<SendContext<T>> pipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);

    public Task Send<T>(object message, IPipe<SendContext> pipe, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromException(_exception);
}
