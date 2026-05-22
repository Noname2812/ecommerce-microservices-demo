using MassTransit;
using MediatR;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductStatusChanged;

public sealed class ProductStatusChangedProjectionConsumer(ISender sender) : IConsumer<ProductStatusChangedV1>
{
    public Task Consume(ConsumeContext<ProductStatusChangedV1> context)
        => sender.Send(
            new RefreshProductProjectionCommand(context.Message.ProductId),
            context.CancellationToken);
}
