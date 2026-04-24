using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;

namespace UrbanX.Search.Application.Messaging
{
    public sealed class ProductCatalogUpdatedConsumer
        : IntegrationEventConsumerBase<
            ProductUpdateIntegrationEvents.ProductCatalogUpdatedV1,
            ProductCatalogUpdatedConsumer>
    {
        public ProductCatalogUpdatedConsumer(
            IMediator mediator,
            ILogger<ProductCatalogUpdatedConsumer> logger)
            : base(mediator, logger)
        {
        }

        protected override Task HandleAsync(
            ProductUpdateIntegrationEvents.ProductCatalogUpdatedV1 @event,
            CancellationToken cancellationToken)
        {
            _ = @event;
            return Task.CompletedTask;
        }
    }
}
