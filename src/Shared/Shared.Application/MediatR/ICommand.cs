using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Application
{

    /// <summary>
    /// Marks a command as idempotent. Supply a unique IdempotencyKey per request.
    /// The behavior stores the result in IDistributedCache for the specified TTL
    /// and short-circuits with the cached result on duplicate submissions.
    /// </summary>
    public interface IIdempotentCommand
    {
        string IdempotencyKey { get; }
        TimeSpan? IdempotencyTtl { get; }
    }

    public interface ICommandBase { }
    public interface ICommand : ICommandBase, IRequest<Result>
    {
    }

    public interface ICommand<TResponse> : ICommandBase, IRequest<Result<TResponse>>
    {
    }
}
