using MassTransit;
using MediatR;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductVariantAdded;

public sealed class ProductVariantAddedProjectionConsumer(ISender sender) : IConsumer<ProductVariantAddedV1>
{
    public Task Consume(ConsumeContext<ProductVariantAddedV1> context)
        => sender.Send(
            new RefreshProductProjectionCommand(context.Message.ProductId),
            context.CancellationToken);
}
