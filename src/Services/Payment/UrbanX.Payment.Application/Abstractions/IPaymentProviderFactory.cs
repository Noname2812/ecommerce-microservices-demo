using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Application.Abstractions;

public interface IPaymentProviderFactory
{
    Result<IPaymentSessionProvider> GetSessionProvider(string method);

    Result<IPaymentRefundProvider> GetRefundProvider(string method);
}
