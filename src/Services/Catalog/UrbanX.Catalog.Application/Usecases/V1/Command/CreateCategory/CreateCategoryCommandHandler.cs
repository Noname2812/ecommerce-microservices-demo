using Shared.Application;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
    {
        public Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
        {
            // Implementation for creating a category
            throw new NotImplementedException();
        }
    }
}