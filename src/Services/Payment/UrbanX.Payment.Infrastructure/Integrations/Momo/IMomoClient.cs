using UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo;

public interface IMomoClient
{
    Task<MomoCreateResponse> CreateSessionAsync(MomoCreateRequest request, CancellationToken cancellationToken);

    Task<MomoRefundResponse> RefundAsync(MomoRefundRequest request, CancellationToken cancellationToken);
}
