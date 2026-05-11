using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Application
{
    public interface IQueryBase { }

    public interface IQuery<TResponse> : IQueryBase, IRequest<Result<TResponse>>
    {
    }
}
