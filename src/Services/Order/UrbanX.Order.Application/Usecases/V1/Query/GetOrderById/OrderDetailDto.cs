namespace UrbanX.Order.Application.Usecases.V1.Query;

public record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    Guid UserId,
    string CustomerEmail,
    string CustomerName,
    string Status,
    string PaymentStatus,
    decimal Subtotal,
    decimal ShippingFee,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? CouponCode,
    string? TrackingNumber,
    string? CancelledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusHistoryDto> StatusHistory
);

public record ShippingAddressDto(
    string Street,
    string? Ward,
    string District,
    string City,
    string Country,
    string RecipientName,
    string RecipientPhone
);

public record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string VariantSku,
    string? VariantName,
    string SellerName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    string? ImageUrl,
    string Status
);

public record OrderStatusHistoryDto(
    string? FromStatus,
    string ToStatus,
    string? Note,
    DateTimeOffset CreatedAt
);
