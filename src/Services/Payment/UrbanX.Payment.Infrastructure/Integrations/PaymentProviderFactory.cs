using System.Collections.Frozen;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Domain.Errors;

namespace UrbanX.Payment.Infrastructure.Integrations;

internal sealed class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly FrozenDictionary<string, IPaymentSessionProvider> _sessionMap;
    private readonly FrozenDictionary<string, IPaymentRefundProvider> _refundMap;

    public PaymentProviderFactory(
        IEnumerable<IPaymentSessionProvider> sessionProviders,
        IEnumerable<IPaymentRefundProvider> refundProviders)
    {
        _sessionMap = sessionProviders.ToFrozenDictionary(
            p => p.Method, StringComparer.OrdinalIgnoreCase);
        _refundMap = refundProviders.ToFrozenDictionary(
            p => p.Method, StringComparer.OrdinalIgnoreCase);
    }

    public Result<IPaymentSessionProvider> GetSessionProvider(string method)
        => _sessionMap.TryGetValue(method, out var provider)
            ? Result.Success(provider)
            : Result.Failure<IPaymentSessionProvider>(PaymentErrors.UnsupportedPaymentMethod);

    public Result<IPaymentRefundProvider> GetRefundProvider(string method)
        => _refundMap.TryGetValue(method, out var provider)
            ? Result.Success(provider)
            : Result.Failure<IPaymentRefundProvider>(PaymentErrors.UnsupportedPaymentMethod);
}
