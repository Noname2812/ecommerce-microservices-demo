using MassTransit;
using MediatR;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductIntegrationEvents;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductCreated;

public sealed class ProductCreatedProjectionConsumer(ISender sender) : IConsumer<ProductCreatedV1>
{
    public Task Consume(ConsumeContext<ProductCreatedV1> context)
        => sender.Send(
            new RefreshProductProjectionCommand(context.Message.ProductId),
            context.CancellationToken);
}
