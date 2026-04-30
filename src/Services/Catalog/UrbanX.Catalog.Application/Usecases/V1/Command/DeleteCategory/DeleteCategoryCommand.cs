using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Command;
public record DeleteCategoryCommand(Guid CategoryId) : ICommand;