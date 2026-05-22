using MassTransit;
using MediatR;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductInfoUpdated;

public sealed class ProductInfoUpdatedProjectionConsumer(ISender sender) : IConsumer<ProductInfoUpdatedV1>
{
    public Task Consume(ConsumeContext<ProductInfoUpdatedV1> context)
        => sender.Send(
            new RefreshProductProjectionCommand(context.Message.ProductId),
            context.CancellationToken);
}
