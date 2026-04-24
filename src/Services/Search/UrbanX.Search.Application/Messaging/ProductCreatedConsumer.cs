using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;

namespace UrbanX.Search.Application.Messaging
{
    public class ProductCreatedConsumer
        : IntegrationEventConsumerBase<
            ProductIntegrationEvents.ProductCreatedV1,
            ProductCreatedConsumer>
    {
        public ProductCreatedConsumer(
            IMediator mediator,
            ILogger<ProductCreatedConsumer> logger)
            : base(mediator, logger)
        {
        }

        protected override async Task HandleAsync(ProductIntegrationEvents.ProductCreatedV1 @event, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}
