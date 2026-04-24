using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Application
{
    public interface IQuery<TResponse> : IRequest<Result<TResponse>>
    {
    }
}
