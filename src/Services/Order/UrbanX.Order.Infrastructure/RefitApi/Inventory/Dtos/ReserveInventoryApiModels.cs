namespace UrbanX.Order.Infrastructure.RefitApi.Inventory.Dtos;

/// <summary>JSON body aligned with Inventory reserve command (idempotency key + line items).</summary>
public sealed record ReserveInventoryApiBody(
    string IdempotencyKey,
    IReadOnlyList<ReserveInventoryApiLineItem> Items);

public sealed record ReserveInventoryApiLineItem(Guid ProductId, int Quantity);
