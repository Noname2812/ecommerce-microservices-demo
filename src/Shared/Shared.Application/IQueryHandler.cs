using MediatR;
using Shared.Contract.Common;

namespace Shared.Application
{
    public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
     where TQuery : IQuery<TResponse>
    {
    }
}
