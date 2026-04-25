using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;

namespace UrbanX.Search.Application.Messaging
{
    public sealed class ProductInfoUpdatedConsumer
        : IntegrationEventConsumerBase<
            ProductUpdateIntegrationEvents.ProductInfoUpdatedV1,
            ProductInfoUpdatedConsumer>
    {
        public ProductInfoUpdatedConsumer(
            IMediator mediator,
            ILogger<ProductInfoUpdatedConsumer> logger)
            : base(mediator, logger)
        {
        }

        protected override Task HandleAsync(
            ProductUpdateIntegrationEvents.ProductInfoUpdatedV1 @event,
            CancellationToken cancellationToken)
        {
            _ = @event;
            return Task.CompletedTask;
        }
    }
}
