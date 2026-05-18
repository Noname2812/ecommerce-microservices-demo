namespace UrbanX.Order.Application.Abstractions;

public interface IOrderTicketStatusQuery
{
    Task<OrderTicketSagaSnapshot?> GetSagaByTicketIdAsync(Guid ticketId, CancellationToken ct = default);
}

public record OrderTicketSagaSnapshot(
    string CurrentState,
    string? FailureReason,
    string? ValidationError,
    DateTimeOffset? PaymentExpiresAt);
