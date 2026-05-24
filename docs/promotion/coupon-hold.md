# Coupon Hold (Cart-time reservation)

Endpoint cho phép Cart đặt giữ (hold) một coupon trước khi user vào checkout. Mục tiêu: **rời coupon khỏi saga critical path** trong Order service — giảm thread pool starvation khi nhiều order cùng dùng coupon.

## Mục đích

**Trước Phase 3** — Order saga gọi Promotion qua event `ClaimCouponRequestedV1` ngay trong luồng PlaceOrder. Mỗi order có coupon = thêm 1 saga step (`CouponClaiming`, 5s timeout) + 4 DB ops + 2 Redis Lua trên Promotion service. Khi peak nhiều order cùng dùng coupon → DB pool + thread pool Promotion bị saturate.

**Sau Phase 3** — coupon được **hold ở Cart screen** (Redis-only, không DB write):

1. **Cart**: user nhập coupon → `POST /api/v1/promotion/coupon-holds` → Promotion trả `HoldToken` + discount info (TTL 15 phút).
2. **Checkout**: client gửi `HoldToken` (không phải `CouponCode`) trong `PlaceOrderCommand` → Order saga verify token bằng 1 Redis GET, **không** call Promotion service trong saga.
3. **Sau payment success**: saga publish `CouponClaimRequestedV1` fire-and-forget (off critical path) → Promotion làm DB claim async (`ClaimCouponCommandHandler` nhận field `HoldToken` để skip Redis acquire, vì Cart đã consume).

**Trạng thái hiện tại** (2026-05):
- Phase 3a: ✅ endpoint Hold/Release
- Phase 3b: ✅ `CouponHoldToken` đã wire vào `PlaceOrderCommand` + `PlaceOrderRequestedV1` + saga state
- Phase 3c: ✅ saga refactor — bỏ state `CouponClaiming`, resolve hold ở Initial, claim post-payment fire-and-forget

## Endpoints

### `POST /api/v1/promotion/coupon-holds`

Reserve một coupon. Request body:

```json
{
  "couponCode": "SUMMER25",
  "userId": "8f3d4f31-0e3c-4f88-9c5e-7d5a0f0b1c00",
  "orderAmount": 1250000
}
```

Response 201 Created (Location header: `/api/v1/promotion/coupon-holds/{token}`):

```json
{
  "holdToken": "a4f3d8c5e1b94a4f8b2c1d3e4f5a6b7c",
  "discountAmount": 125000.0,
  "discountType": "Percentage",
  "expiresAt": "2026-05-23T15:30:00Z"
}
```

Error codes (cùng convention `CouponErrors`):

| Code | HTTP | Khi nào |
|---|---|---|
| `COUPON_NOT_FOUND` | 422 | Code không tồn tại trong DB |
| `COUPON_INACTIVE` | 422 | `coupon.IsActive == false` |
| `COUPON_EXPIRED` | 422 | Ngoài window `[ValidFrom, ExpiresAt]` |
| `ORDER_BELOW_MIN_VALUE` | 422 | `orderAmount < coupon.MinOrderValue` |
| `COUPON_ALREADY_USED` | 409 | User đã hold/claim coupon này (Redis user-lock SET NX trượt) |
| `COUPON_EXHAUSTED` | 409 | Hết quota toàn cục |

### `DELETE /api/v1/promotion/coupon-holds/{token}`

Release hold trước TTL — dùng khi user xoá coupon ở Cart. Idempotent: 204 No Content kể cả khi token đã expire/không tồn tại.

## Cách hoạt động (Redis-only)

```
POST /coupon-holds
  ├── DB SELECT coupon WHERE id = :code   (1 read — metadata: discount, validity, quota)
  ├── Validate window (active, in [ValidFrom, ExpiresAt], orderAmount >= MinOrderValue)
  ├── Redis Lua: SET NX coupon:{code}:user:{userId} EX 900   (user-hold lock, 15 min)
  ├── Redis Lua: SET NX coupon:{code}:quota init=remaining + DECR + rollback nếu < 0  (quota slot)
  ├── Generate HoldToken = Guid.NewGuid().ToString("N")
  └── Redis SET coupon:hold:{token} = JSON{CouponHoldInfo} EX 900
```

Không có DB write. So với `ClaimCouponCommand` cũ (4 DB op + 2 Redis Lua + transaction wrap), hold cost = **1 DB read + 3 Redis ops, không transaction**.

## TTL & cleanup

- `coupon:hold:{token}` — TTL 15 phút (Redis tự xoá)
- `coupon:{code}:user:{userId}` — TTL 15 phút (Redis tự xoá)
- `coupon:{code}:quota` — không TTL (counter ổn định)
- Nếu user abandon Cart → Redis tự release sau 15 phút, không cần job

## Token format

`Guid.NewGuid().ToString("N")` — 32 hex chars, đủ collision-resistant trong window 15 phút. Token chỉ có ý nghĩa runtime, không persist DB. Client cần lưu trong local state cho đến checkout.

## Edge cases

- **Re-hold cùng coupon cho cùng user**: trả `COUPON_ALREADY_USED` (Redis SET NX user-lock fail). Client phải release token cũ trước.
- **User đổi `orderAmount` ở Cart**: token cũ vẫn valid (discount đã calc theo amount lúc hold). Nếu cần re-calc → release rồi hold lại.
- **Redis flush giữa hold và checkout**: token không còn → checkout (Phase 3b) trả lỗi `COUPON_HOLD_EXPIRED`, client phải hold lại.
- **Concurrent hold race**: Redis SET NX guarantee single-winner. Loser nhận `COUPON_ALREADY_USED`.

## Files liên quan

- `Application/Usecases/V1/Command/HoldCoupon/HoldCouponCommand.cs` + Handler
- `Application/Usecases/V1/Command/ReleaseCouponHold/ReleaseCouponHoldCommand.cs` + Handler
- `Application/Abstractions/ICouponHoldGateway.cs`
- `Infrastructure/Redis/CouponHoldGateway.cs`
- `API/Apis/CouponHoldApis.cs`
- `Domain/Constants/CouponRedisKeys.cs` — `Hold(token)` key

## Order saga integration (Phase 3b/3c)

Xem [docs/order/place-order-normal-coupon.md](../order/place-order-normal-coupon.md) cho luồng saga đầy đủ sau Phase 3.

Tóm tắt cross-service contract trên Redis (hai bên giữ key format đồng bộ):

| Key | Producer | Consumer | TTL |
|---|---|---|---|
| `coupon:hold:{token}` | Promotion (`CouponHoldGateway.SetHoldAsync`) | Order (`CouponHoldClient.TryGetAsync`) | 15m |
| `coupon:{code}:user:{userId}` | Promotion (Cart Lua SET NX) | Order (release Lua DEL) | 15m |
| `coupon:{code}:quota` | Promotion (Cart Lua DECR) | Order (release Lua INCR) | none |

Sai lệch key format giữa 2 service = bug silent (Order không tìm thấy hold → order fail với `COUPON_HOLD_EXPIRED`). Khi đổi convention, sửa cả [CouponRedisKeys.cs](../../src/Services/Promotion/UrbanX.Promotion.Domain/Constants/CouponRedisKeys.cs) (Promotion) và [CouponHoldClient.cs](../../src/Services/Order/UrbanX.Order.Infrastructure/Services/CouponHoldClient.cs) (Order) cùng commit.

## Còn lại (deferred)

- Frontend (`urbanx-react`) wire endpoint Hold vào Cart screen — outside .NET solution scope
- Sales flow vẫn dùng [RedisCouponLockService](../../src/Services/Order/UrbanX.Order.Infrastructure/Services/RedisCouponLockService.cs) (Order-side lock at saga time). Nếu muốn unify, áp dụng cùng pattern Hold cho Sales sau.
