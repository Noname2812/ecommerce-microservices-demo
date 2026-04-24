using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Application
{
    public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
     where TQuery : IQuery<TResponse>
    {
    }
}
