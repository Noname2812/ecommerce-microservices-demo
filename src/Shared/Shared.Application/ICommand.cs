using MediatR;
using Shared.Contract.Common;

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
