using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Kernel.Primitives;
using System.Text.RegularExpressions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Integrations.SePay;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ResolveSePayWebhookPayment;

internal sealed class ResolveSePayWebhookPaymentQueryHandler(
    IPaymentRepository paymentRepository,
    IOptionsSnapshot<SePayOptions> sePayOptions,
    ILogger<ResolveSePayWebhookPaymentQueryHandler> logger) : IQueryHandler<ResolveSePayWebhookPaymentQuery, Guid?>
{
    public async Task<Result<Guid?>> Handle(ResolveSePayWebhookPaymentQuery request, CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        var limit = sePayOptions.Value.WebhookMemoMatchCandidateLimit;
        var candidates = await paymentRepository.FindSePayMatchCandidatesAsync(content, limit, cancellationToken);

        var matched = candidates
            .Where(p => !string.IsNullOrEmpty(p.OrderNumber) &&
                        Regex.IsMatch(
                            content,
                            SePayIntegrationConstants.OrderNumberRegexWordBoundary +
                            Regex.Escape(p.OrderNumber) +
                            SePayIntegrationConstants.OrderNumberRegexWordBoundary,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();

        if (matched.Count == 0)
            return Result.Success<Guid?>((Guid?)null);

        if (matched.Count == 1)
            return Result.Success<Guid?>(matched[0].Id);

        var resolved = TryResolveSinglePaymentId(matched);
        if (resolved.HasValue)
            return Result.Success<Guid?>(resolved.Value);

        logger.LogWarning(
            "SePay webhook content matched {Count} payments after regex; could not pick a single target.",
            matched.Count);
        return Result.Success<Guid?>((Guid?)null);
    }

    /// <summary>
    /// When ILike returns multiple rows, prefer one open payment, else one expired (late transfer),
    /// else one in-flight processing, else one completed (idempotent "already completed").
    /// </summary>
    private Guid? TryResolveSinglePaymentId(IReadOnlyList<PaymentEntity> matched)
    {
        var open = matched.Where(p =>
            p.Status is PaymentStatus.Pending or PaymentStatus.PartiallyPaid).ToList();
        if (open.Count == 1)
            return open[0].Id;
        if (open.Count > 1)
            return null;

        var expired = matched.Where(p => p.Status == PaymentStatus.Expired).ToList();
        if (expired.Count == 1)
            return expired[0].Id;
        if (expired.Count > 1)
            return null;

        var processing = matched.Where(p => p.Status == PaymentStatus.Processing).ToList();
        if (processing.Count == 1)
            return processing[0].Id;
        if (processing.Count > 1)
            return null;

        var completed = matched.Where(p => p.Status == PaymentStatus.Completed).ToList();
        if (completed.Count == 1)
            return completed[0].Id;

        return null;
    }
}
