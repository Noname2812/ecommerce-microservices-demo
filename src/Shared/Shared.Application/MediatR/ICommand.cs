using MediatR;
using Shared.Kernel.Primitives;

namespace Shared.Application
{
    public interface ICommandBase { }
    public interface ICommand : ICommandBase, IRequest<Result>
    {
    }

    public interface ICommand<TResponse> : ICommandBase, IRequest<Result<TResponse>>
    {
    }
}
