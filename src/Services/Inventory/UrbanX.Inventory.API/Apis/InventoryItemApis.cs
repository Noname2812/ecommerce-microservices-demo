using Carter;
using MediatR;
using UrbanX.Inventory.API.Abstractions;

namespace UrbanX.Inventory.API.Apis;

public class InventoryItemApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/inventory/items";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("InventoryItem")
            .MapGroup(BaseURL).HasApiVersion(1);

        // Endpoints sẽ được thêm theo từng use case (add-command / add-query skill)
    }
}
