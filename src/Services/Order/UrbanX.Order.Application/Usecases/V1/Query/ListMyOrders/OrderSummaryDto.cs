namespace UrbanX.Order.Application.Usecases.V1.Query;

public record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    decimal TotalAmount,
    int ItemCount,
    DateTimeOffset CreatedAt
);
