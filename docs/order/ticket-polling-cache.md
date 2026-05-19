# Order Ticket Polling — Cache Strategy

> Status: Approved 2026-05-19 — implemented
> Endpoint: `GET /api/v1/orders/ticket/{ticketId}`
> Handler: [GetOrderByTicketQueryHandler](../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Query/GetOrderByTicket/GetOrderByTicketQueryHandler.cs)

## Mục đích

Endpoint polling cho client theo dõi tiến trình đặt hàng async. Tần suất poll cao (vài giây/lần), nhiều client cùng hỏi cùng một `ticketId` → bắt buộc cache + stampede protection để DB không gục.

## Chiến lược cache

- **Cache key**: `order:ticket:{ticketId}`
- **TTL động** (`ExpirySelector`):
  - Terminal status (`CONFIRMED` / `CANCELLED`) → `TerminalTtlSeconds` (mặc định **300s**) — trạng thái không đổi nữa, cache lâu để giảm DB hit.
  - Non-terminal (`PROCESSING` / `PENDING_PAYMENT`) → `NonTerminalTtlSeconds` (mặc định **2s**) — đủ tươi để client nhận update kịp thời.
- **SingleFlight = ON**: 1000 client poll cùng `ticketId` lúc cache miss → 1 DB query duy nhất, 999 follower await leader.
- **Distributed lock = OFF**: TTL non-terminal chỉ 2s, lock overhead lớn hơn DB hit redundant từ một vài process khác.
- **Cache invalidation**: `OrderConfirmedCacheConsumer` + `OrderCancelledCacheConsumer` gọi `RemoveAsync(order:ticket:{id})` khi nhận `OrderConfirmedV1` / `OrderCancelledV1` → client thấy trạng thái cuối tức thì thay vì chờ TTL.

## Hành vi khi Redis fail

Nhờ `RedisCacheService` đã wrap với circuit breaker + try/catch:
- `GetAsync` trả `null` (silent miss) → factory chạy → DB query trực tiếp.
- `SetAsync` log warn + no-op → response vẫn 200 cho client.
- SingleFlight vẫn hoạt động vì là cơ chế in-process (không cần Redis) → vẫn coalesce concurrent miss.

→ Endpoint không bao giờ trả 500 do Redis chết.

## Configuration

```json
// appsettings.json
{
  "Order": {
    "TicketCache": {
      "TerminalTtlSeconds": 300,
      "NonTerminalTtlSeconds": 2
    }
  }
}
```

Class: [OrderTicketCacheOptions](../../src/Services/Order/UrbanX.Order.Application/DependencyInjection/Options/OrderTicketCacheOptions.cs).

## Trade-offs

| Lựa chọn | Đánh đổi |
|---|---|
| TTL non-terminal 2s | Client thấy state mới sau ≤ 2s; DB QPS giảm ~50× so với không cache |
| Không distributed lock | Cross-process stampede có thể gây ≤ N replicas × 1 DB query trong cửa sổ 2s — chấp nhận được |
| Cache cả `Result<T>` thay vì `OrderTicketStatusDto` | Cache cả `Forbidden` / `NotFound` error → tránh DB hit lại; client nhận lỗi cùng tốc độ |
| Negative TTL không bật | Ticket không tồn tại → mỗi poll vẫn hit DB; có thể bật `NegativeTtl = 1s` nếu thấy spam scan |

## Liên quan

- [Shared.Cache resilience](../shared/shared-cache.md)
- [Async Ticket Flow](async-ticket-flow.md) — toàn cảnh saga
