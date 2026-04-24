using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;

namespace UrbanX.Search.Application.Messaging
{
    public sealed class ProductDeletedSearchConsumer
        : IntegrationEventConsumerBase<
            ProductUpdateIntegrationEvents.ProductDeletedV1,
            ProductDeletedSearchConsumer>
    {
        public ProductDeletedSearchConsumer(
            IMediator mediator,
            ILogger<ProductDeletedSearchConsumer> logger)
            : base(mediator, logger)
        {
        }

        protected override Task HandleAsync(
            ProductUpdateIntegrationEvents.ProductDeletedV1 @event,
            CancellationToken cancellationToken)
        {
            _ = @event;
            return Task.CompletedTask;
        }
    }
}
