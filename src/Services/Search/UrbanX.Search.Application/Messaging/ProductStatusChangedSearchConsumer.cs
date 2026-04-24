using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;

namespace UrbanX.Search.Application.Messaging
{
    public sealed class ProductStatusChangedSearchConsumer
        : IntegrationEventConsumerBase<
            ProductUpdateIntegrationEvents.ProductStatusChangedV1,
            ProductStatusChangedSearchConsumer>
    {
        public ProductStatusChangedSearchConsumer(
            IMediator mediator,
            ILogger<ProductStatusChangedSearchConsumer> logger)
            : base(mediator, logger)
        {
        }

        protected override Task HandleAsync(
            ProductUpdateIntegrationEvents.ProductStatusChangedV1 @event,
            CancellationToken cancellationToken)
        {
            _ = @event;
            return Task.CompletedTask;
        }
    }
}
