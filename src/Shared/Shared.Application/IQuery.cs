using MediatR;
using Shared.Contract.Common;

namespace Shared.Application
{
    public interface IQuery<TResponse> : IRequest<Result<TResponse>>
    {
    }
}
