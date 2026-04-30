using Shared.Application;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand, Guid>
    {
        public Task<Result<Guid>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
        {
            // Implementation for updating a category
            throw new NotImplementedException();
        }
    }
}