using MassTransit;
using MediatR;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductVariantDeleted;

public sealed class ProductVariantDeletedProjectionConsumer(ISender sender) : IConsumer<ProductVariantDeletedV1>
{
    public Task Consume(ConsumeContext<ProductVariantDeletedV1> context)
        => sender.Send(
            new RefreshProductProjectionCommand(context.Message.ProductId),
            context.CancellationToken);
}
