namespace Shared.Application
{
    /// <summary>
    /// Abstraction for request/response messaging (MassTransit RequestClient).
    /// </summary>
    public interface IMessageRequestClient
    {
        Task<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class;
    }
}
