using Shared.Application;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public sealed class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand>
    {
        public Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
        {
            // Implementation for deleting a category
            throw new NotImplementedException();
        }
    }
}