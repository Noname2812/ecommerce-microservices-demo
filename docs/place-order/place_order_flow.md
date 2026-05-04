# Place Order — Technical Design Document

**Version:** 1.0  
**Status:** Draft  
**Service scope:** Order Service · Inventory Service · Coupon Service  
**Scale target:** 50–100 req/s normal load  
**Stack:** .NET 8 · EF Core · MassTransit · Redis · PostgreSQL / SQL Server

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
4. [API Contracts](#4-api-contracts)
5. [Flow Details](#5-flow-details)
6. [Failure Handling](#6-failure-handling)
7. [Concurrency Strategy](#7-concurrency-strategy)
8. [Test Cases](#8-test-cases)
9. [Non-Functional Requirements](#9-non-functional-requirements)

---

## 1. Overview

### 1.1 Problem Statement

Hệ thống ecommerce distributed cần đảm bảo:
- **Không save order rác** — chỉ INSERT order khi inventory và coupon đã được giữ thành công
- **Không oversell** — không bao giờ bán vượt số hàng tồn kho
- **Không double-claim coupon** — một user không thể dùng cùng coupon 2 lần trong cùng thời điểm
- **Eventual consistency** — các service không cần sync real-time, tự phục hồi qua TTL và compensation events

### 1.2 Core Design Decisions

| Quyết định | Lý do |
|---|---|
| Reserve trước, Save sau | Tránh order rác khi inventory/coupon fail |
| Sync call cho Reserve/Claim | Biết kết quả ngay trước khi save, trade-off latency ~10–20ms chấp nhận được ở 50–100 req/s |
| TTL trên mọi reservation | Safety net không phụ thuộc vào process còn sống hay không |
| Outbox pattern | Đảm bảo event không mất dù crash sau khi commit DB |
| Optimistic locking cho inventory | Không dùng row lock, phù hợp với 50–100 req/s, retry ×3 đủ |
| Redis SETNX cho coupon | Atomic claim per-user, không race condition |

### 1.3 Out of Scope (tài liệu này)

- Payment flow (sau khi order CONFIRMED)
- Order cancellation
- Flash sale (tài liệu riêng)
- Notification Service

---

## 2. Architecture

### 2.1 System Context

```
┌─────────────┐     POST /orders      ┌─────────────────┐
│   Client    │ ──────────────────▶   │   API Gateway   │
│  Web / App  │                       │  Auth · RL · IK  │
└─────────────┘                       └────────┬────────┘
                                               │
                                               ▼
                                    ┌─────────────────────┐
                          ┌─sync──  │    Order Service    │  ──async──▶ Message Broker
                          │         │  Orchestrate flow   │             (OrderConfirmed)
                          │  ┌sync─ └─────────────────────┘
                          │  │
                          ▼  ▼
              ┌───────────────────┐    ┌───────────────────┐
              │ Inventory Service │    │  Coupon Service   │
              │  Reserve/Release  │    │  Claim/Release    │
              └───────────────────┘    └───────────────────┘
```

### 2.2 Service Ownership

| Service | Owns | Does NOT own |
|---|---|---|
| Order Service | Orders table, Outbox, CompensationOutbox, Saga orchestration | Inventory state, Coupon state |
| Inventory Service | Inventory table, Reservations table, TTL job | Order state |
| Coupon Service | Coupons table, CouponClaims table, Redis quota, TTL job | Order state |

### 2.3 Communication Patterns

- **Order → Inventory:** Synchronous HTTP/gRPC (Reserve, Release)
- **Order → Coupon:** Synchronous HTTP/gRPC (Claim, Release)
- **Order → Broker:** Async via Outbox (OrderConfirmed → Payment flow)
- **Compensation:** Async via CompensationOutbox → Broker → Inventory/Coupon consumers

---

## 4. API Contracts

### 4.1 Place Order — Order Service

#### Request
```
POST /api/v1/orders
Headers:
  Authorization:    Bearer {jwt}
  Idempotency-Key:  {uuid-v4}           -- BẮT BUỘC
  Content-Type:     application/json
```

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    { "productId": 101, "quantity": 2 },
    { "productId": 205, "quantity": 1 }
  ],
  "couponCode": "SALE50",
  "shippingAddress": {
    "fullName": "Nguyen Van A",
    "phone": "0901234567",
    "address": "123 Nguyen Hue",
    "district": "Quan 1",
    "city": "Ho Chi Minh",
    "zipCode": "70000"
  },
  "pricingSnapshot": {
    "originalPrice": 500000,
    "finalPrice": 425000,
    "capturedAt": "2024-01-01T00:00:00Z"
  }
}
```

#### Responses

**201 Created — Happy path**
```json
{
  "orderId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "CONFIRMED",
  "finalAmount": 425000,
  "createdAt": "2024-01-01T00:00:05Z"
}
```

**400 Bad Request — Input validation failed**
```json
{
  "type": "VALIDATION_ERROR",
  "errors": [
    { "field": "items[0].quantity", "message": "Quantity must be between 1 and 100" },
    { "field": "shippingAddress.phone", "message": "Invalid phone format" }
  ]
}
```

**409 Conflict — Business conflict**
```json
{
  "type": "OUT_OF_STOCK",
  "message": "Product 101 is out of stock",
  "detail": { "productId": 101, "requested": 2, "available": 1 }
}
```
```json
{
  "type": "COUPON_EXHAUSTED",
  "message": "Coupon SALE50 has no remaining quota"
}
```
```json
{
  "type": "COUPON_ALREADY_USED",
  "message": "You have already used coupon SALE50"
}
```

**422 Unprocessable Entity — Business rule violation**
```json
{
  "type": "PRICE_MISMATCH",
  "message": "Price has changed since your last quote. Please refresh and try again.",
  "detail": { "quotedPrice": 500000, "currentPrice": 520000 }
}
```

**429 Too Many Requests**
```json
{
  "type": "RATE_LIMITED",
  "message": "Too many order requests. Please wait before trying again.",
  "retryAfter": 60
}
```

**503 Service Unavailable — Dependency down**
```json
{
  "type": "SERVICE_UNAVAILABLE",
  "message": "Unable to process order at this time. Please try again.",
  "retryAfter": 30
}
```

---

### 4.2 Reserve — Inventory Service (internal)

```
POST /internal/v1/reservations
Headers:
  Idempotency-Key: {uuid}
```
```json
{
  "idempotencyKey": "order-key:inv",
  "items": [{ "productId": 101, "quantity": 2 }]
}
```
**201:**
```json
{
  "reservationId": "uuid",
  "expiresAt": "2024-01-01T00:15:00Z"
}
```
**409:** `{ "type": "OUT_OF_STOCK", "productId": 101 }`

---

### 4.3 Claim — Coupon Service (internal)

```
POST /internal/v1/coupon-claims
```
```json
{
  "idempotencyKey": "order-key:cpn",
  "couponCode": "SALE50",
  "userId": "uuid",
  "orderAmount": 500000
}
```
**201:**
```json
{
  "claimId": "uuid",
  "discountAmount": 250000,
  "expiresAt": "2024-01-01T00:15:00Z"
}
```
**409:** `{ "type": "COUPON_EXHAUSTED" }` hoặc `{ "type": "COUPON_ALREADY_USED" }`

---

## 5. Flow Details

### 5.1 Happy Path

```
Client                Order Service          Inventory Svc       Coupon Svc       DB
  │                        │                      │                   │             │
  │── POST /orders ────────▶                      │                   │             │
  │   Idempotency-Key: IK  │                      │                   │             │
  │                        │── Layer 1: Input ────│                   │             │
  │                        │   validation (sync)  │                   │             │
  │                        │                      │                   │             │
  │                        │── Layer 2: Rate ──────────────────────────────────────▶│ Redis
  │                        │   limit check        │                   │             │
  │                        │                      │                   │             │
  │                        │── Layer 3: Business rules (Task.WhenAll) ──────────────▶│ DB read
  │                        │   Products active? Price match?          │             │
  │                        │                      │                   │             │
  │                        │── POST /reservations ▶                   │             │
  │                        │   IK: IK:inv         │ reserve + ExpiresAt             │
  │                        │◀─ 201 reservationId ─│                   │             │
  │                        │                      │                   │             │
  │                        │─── POST /coupon-claims ─────────────────▶│             │
  │                        │    IK: IK:cpn        │                   │ claim + TTL │
  │                        │◀── 201 claimId ──────────────────────────│             │
  │                        │                      │                   │             │
  │                        │─── BEGIN TRANSACTION ──────────────────────────────────▶│
  │                        │    INSERT Orders (CONFIRMED)             │             │
  │                        │    INSERT Outbox (OrderConfirmed)        │             │
  │                        │─── COMMIT ──────────────────────────────────────────────▶│
  │                        │                      │                   │             │
  │                        │─── Cache IK response ─────────────────────────────────▶│ Redis
  │◀── 201 orderId ────────│                      │                   │             │
  │                        │                      │                   │             │
  │              [async] OutboxWorker publishes OrderConfirmed → Broker             │
```

### 5.2 Idempotency Key Flow

```
Request đến với IK = "abc-123"
        │
        ▼
Redis GET "idempotency:abc-123"
        │
   ┌────┴────┐
   │  EXISTS  │                     │  NOT EXISTS  │
   ▼          │                     ▼              │
Trả cached   │              Xử lý request          │
response     │              (flow bình thường)      │
             │                     │               │
             │              Sau khi thành công:    │
             │              Redis SET "idempotency:abc-123"
             │              = {statusCode, body}    │
             │              TTL = 24h              │
```

**Idempotency key format cho downstream:**
- Inventory reserve: `{originalIK}:inv`
- Coupon claim: `{originalIK}:cpn`

Đảm bảo client retry với cùng IK → downstream cũng nhận cùng derived key → trả lại kết quả cũ, không tạo reservation/claim mới.

---

## 6. Failure Handling

### 6.1 Failure Matrix

| Bước | Loại lỗi | Trạng thái | Compensation cần | Cách xử lý |
|---|---|---|---|---|
| Layer 1–3 (validation) | Validation / Business rule | Không có resource nào | Không cần | Trả 400/422 ngay |
| Layer 2 (rate limit) | Quá ngưỡng | Không có resource nào | Không cần | Trả 429 ngay |
| Step 4 (Reserve inventory) | Hết hàng, timeout | Không có | Không cần | Trả 409/503 |
| Step 5 (Claim coupon) | Hết quota, đã dùng | Inventory đã reserve | Release inventory | CompensationOutbox → InventoryReleaseRequested |
| Step 5 (Claim coupon) | Timeout / 5xx | Inventory đã reserve | Release inventory | CompensationOutbox → InventoryReleaseRequested |
| Step 6 (Save order) | DB lỗi, crash sau commit | Inventory + Coupon reserved | Release cả 2 | CompensationOutbox → 2 release events |
| Step 6 (Save order) | Process crash trước khi ghi | Inventory + Coupon reserved | TTL tự release | TTL = 15 phút, không cần can thiệp |
| Step 5 or 6 | CompensationOutbox cũng fail | Inventory + Coupon reserved | TTL tự release | TTL là safety net cuối cùng |

### 6.2 Compensation Detail

#### 6.2.1 Coupon Claim Fail → Release Inventory

```
Order Service (catch block):
  1. Ghi CompensationOutbox:
     Type = "IInventoryReleaseRequested"
     Payload = { reservationId, reason: "COUPON_CLAIM_FAILED", correlationId }

CompensationOutbox Worker (async):
  2. Poll, publish event lên broker "compensation.events"

Inventory Service Consumer:
  3. Nhận IInventoryReleaseRequested
  4. Check ProcessedEvents(eventId) → đã xử lý chưa?
  5. Nếu chưa: Available += qty, Reserved -= qty, Status = RELEASED
  6. INSERT ProcessedEvents(eventId)

TTL Job (safety net, chạy mỗi 2 phút):
  7. Nếu consumer fail nhiều lần:
     Reservation.ExpiresAt < now → tự release
```

#### 6.2.2 Save Order Fail → Release Inventory + Coupon

```
Order Service (catch block):
  1. Ghi 2 rows vào CompensationOutbox:
     - IInventoryReleaseRequested { reservationId }
     - ICouponReleaseRequested    { claimId }
     (dùng separate DB connection, best-effort)

Workers xử lý song song:
  2. Inventory Service nhận → release reservation
  3. Coupon Service nhận → release claim, xóa Redis key, tăng quota

TTL Jobs (safety net):
  4. Cả 2 service đều có TTL job chạy độc lập
```

#### 6.2.3 Server Crash Trước Khi Ghi DB

```
Tình trạng: Inventory reserved, Coupon claimed, Order DB trống, CompensationOutbox trống

Không có code nào có thể chạy. Chỉ có TTL:

Inventory TTL Job (2 phút/lần):
  - Quét: Status='PENDING' AND ExpiresAt < now
  - Release: Available += qty

Coupon TTL Job (2 phút/lần):
  - Quét: Status='CLAIMED' AND ExpiresAt < now
  - Release: Redis key xóa, quota tăng

Client nhận timeout/5xx:
  - Retry với cùng Idempotency-Key
  - Order Service: không tìm thấy IK trong Redis → xử lý lại bình thường
  - Downstream services: derived IK (IK:inv, IK:cpn) có thể còn sống
    → trả lại reservationId/claimId cũ nếu TTL chưa hết
    → tạo mới nếu TTL đã hết
```

### 6.3 Circuit Breaker Behavior

```
Inventory Service hoặc Coupon Service không phản hồi:

Normal:
  5 lần fail liên tiếp trong 30 giây → Circuit OPEN

Circuit OPEN:
  - Fast fail ngay lập tức (< 5ms)
  - Trả 503 cho client với Retry-After header
  - Không tiêu tốn thread pool chờ timeout

Sau 10 giây:
  - Circuit HALF-OPEN: cho 1 request thử
  - Thành công → Circuit CLOSED
  - Fail → Circuit OPEN tiếp

Benefit:
  - Tránh cascade failure
  - Tránh thread starvation
  - Client biết retry sau bao lâu
```

---

## 7. Concurrency Strategy

### 7.1 Inventory — Optimistic Locking

```
Scenario: 100 requests đồng thời, chỉ còn 1 unit

Request 1:  READ  Available=1, RowVersion=0x01
Request 2:  READ  Available=1, RowVersion=0x01
            ...
Request 100: READ Available=1, RowVersion=0x01

Request 1:  UPDATE SET Available=0 WHERE RowVersion=0x01  ✓ SUCCESS, RowVersion → 0x02
Request 2:  UPDATE SET Available=0 WHERE RowVersion=0x01  ✗ FAIL (RowVersion changed)
            → Reload: Available=0 → throw OutOfStockException
            ...
Request 100: Tương tự → OutOfStockException

Kết quả: đúng 1 order thành công, 99 nhận 409
```

**Retry policy:**
- Tối đa 3 lần
- Chỉ retry khi `DbUpdateConcurrencyException`
- Không retry khi `OutOfStockException` (hết hàng thật)

### 7.2 Coupon — Redis Atomic Operations

```
Scenario: 100 requests đồng thời, quota = 5, user khác nhau

Step 1 — Per-user lock (SETNX):
  Request 1:  SETNX coupon:SALE50:user:U1  TTL=15min  → 1 (SUCCESS, key tạo mới)
  Request 2:  SETNX coupon:SALE50:user:U2  TTL=15min  → 1 (SUCCESS)
  ...
  Request 100: SETNX coupon:SALE50:user:U1  → 0 (FAIL, key đã tồn tại từ request 1)

Step 2 — Global quota (DECR):
  Request 1:  DECR coupon:SALE50:quota  → 4  (thành công)
  Request 2:  DECR coupon:SALE50:quota  → 3
  Request 3:  DECR coupon:SALE50:quota  → 2
  Request 4:  DECR coupon:SALE50:quota  → 1
  Request 5:  DECR coupon:SALE50:quota  → 0
  Request 6:  DECR coupon:SALE50:quota  → -1  → INCR lại, xóa user key → 409

Kết quả: đúng 5 claims thành công
```

### 7.3 TTL Values

| Resource | TTL | Lý do |
|---|---|---|
| Inventory reservation | 15 phút | Đủ cho user hoàn thành payment, không block hàng quá lâu |
| Coupon claim (DB) | 15 phút | Sync với inventory |
| Coupon claim (Redis key) | 15 phút | Sync với DB |
| Idempotency key | 24 giờ | Đủ cho retry sau khi ngủ dậy, không quá lâu gây nhầm lẫn |
| Price lock quote | 15 phút | Đủ để checkout, sync với reservation |

---

## 8. Test Cases

### 8.1 Happy Path

#### TC-HP-001: Đặt hàng thành công không có coupon
```
Given:
  - Product 101 có Available = 50
  - User đã đăng nhập, JWT hợp lệ
  - Địa chỉ giao hàng hợp lệ
  - Giá trong request khớp catalog

When:
  POST /api/v1/orders
  { items: [{productId: 101, quantity: 2}], couponCode: null }

Then:
  - Response: 201, orderId trả về
  - Orders table có 1 row, Status = CONFIRMED
  - Inventory: Available = 48, Reserved = 2
  - OutboxMessages có 1 row Type = IOrderConfirmed
  - Redis có idempotency key với response cached
```

#### TC-HP-002: Đặt hàng thành công có coupon
```
Given:
  - Coupon "SALE50" active, quota = 10, user chưa dùng
  - Inventory đủ hàng

When:
  POST /api/v1/orders { couponCode: "SALE50" }

Then:
  - 201, orderId
  - CouponClaims có 1 row Status = CLAIMED
  - Orders.CouponClaimId không null
  - Orders.FinalAmount = OriginalPrice - DiscountAmount
  - Redis: coupon:SALE50:user:{userId} tồn tại
  - Redis: coupon:SALE50:quota = 9
```

#### TC-HP-003: Retry với cùng Idempotency-Key sau thành công
```
Given: TC-HP-001 đã thành công

When:
  Gọi lại với cùng Idempotency-Key

Then:
  - 201 (cùng response)
  - Không có order mới trong DB
  - Inventory không thay đổi
  - Không có reservation mới
```

---

### 8.2 Input Validation Failures

#### TC-V-001: Thiếu Idempotency-Key header
```
When: POST /orders không có header Idempotency-Key
Then: 400, type: MISSING_IDEMPOTENCY_KEY
```

#### TC-V-002: Items rỗng
```
When: items = []
Then: 400, error trên field "items"
```

#### TC-V-003: Quantity = 0
```
When: items = [{productId: 1, quantity: 0}]
Then: 400, error trên field "items[0].quantity"
```

#### TC-V-004: Quantity vượt max (> 100)
```
When: items = [{productId: 1, quantity: 101}]
Then: 400
```

#### TC-V-005: Số lượng items vượt max (> 20)
```
When: items có 21 phần tử
Then: 400
```

#### TC-V-006: Phone format sai
```
When: shippingAddress.phone = "abc123"
Then: 400, error trên field "shippingAddress.phone"
```

---

### 8.3 Auth & Rate Limit Failures

#### TC-AUTH-001: JWT hết hạn
```
When: Authorization: Bearer {expired_token}
Then: 401
```

#### TC-AUTH-002: Rate limit exceeded
```
When: Gửi 6 requests trong 1 phút từ cùng userId
Then: Request thứ 6 nhận 429, header Retry-After = 60
```

---

### 8.4 Business Rule Failures

#### TC-BR-001: Product không tồn tại
```
When: items = [{productId: 99999, quantity: 1}]
Then: 422, type: PRODUCT_NOT_FOUND
```

#### TC-BR-002: Product inactive
```
Given: Product 101 IsActive = false
When: items = [{productId: 101, quantity: 1}]
Then: 422, type: PRODUCT_UNAVAILABLE
```

#### TC-BR-003: Price mismatch
```
Given: Catalog price của product 101 = 300,000
When: pricingSnapshot.originalPrice gửi 250,000 (chênh > 1%)
Then: 422, type: PRICE_MISMATCH, detail có currentPrice
```

#### TC-BR-004: Địa chỉ không giao được
```
Given: Hệ thống không ship đến tỉnh X
When: shippingAddress.city = "Tỉnh X"
Then: 422, type: SHIPPING_NOT_AVAILABLE
```

---

### 8.5 Inventory Failures

#### TC-INV-001: Hết hàng
```
Given: Product 101 Available = 1
When: items = [{productId: 101, quantity: 2}]
Then:
  - 409, type: OUT_OF_STOCK
  - Orders table: không có row mới
  - Coupon: không bị claim
```

#### TC-INV-002: Hết hàng sau nhiều concurrent requests
```
Given: Product 101 Available = 5
When: 10 requests đồng thời, mỗi request quantity = 1
Then:
  - Đúng 5 requests thành công (201)
  - 5 requests còn lại nhận 409
  - Available = 0, Reserved = 5
  - Không có Available âm
```

#### TC-INV-003: Inventory Service timeout
```
Given: Inventory Service không phản hồi sau 5s
When: Gửi order request
Then:
  - 503
  - Orders: không có row mới
  - Coupon: không bị claim
```

#### TC-INV-004: Inventory Service circuit breaker
```
Given: Inventory Service down, đã fail 5 lần liên tiếp
When: Gửi order request
Then:
  - 503 trong < 100ms (fast fail, không chờ timeout)
```

---

### 8.6 Coupon Failures

#### TC-CPN-001: Coupon không tồn tại
```
When: couponCode = "NOTEXIST"
Then:
  - 422, type: COUPON_NOT_FOUND
  - Inventory: release reservation (compensation)
  - Orders: không có row mới
```

#### TC-CPN-002: Coupon hết quota
```
Given: Coupon "SALE50" UsedQuota = TotalQuota = 10
When: Gửi order với couponCode = "SALE50"
Then:
  - 409, type: COUPON_EXHAUSTED
  - Inventory: release reservation
  - Orders: không có row mới
```

#### TC-CPN-003: User đã claim coupon này
```
Given: User đã có CouponClaim CLAIMED cho "SALE50"
When: Gửi order mới với cùng couponCode
Then:
  - 409, type: COUPON_ALREADY_USED
  - Inventory: release reservation
```

#### TC-CPN-004: 100 concurrent requests, quota = 5
```
Given: Coupon quota = 5, 100 users khác nhau
When: 100 requests đồng thời
Then:
  - Đúng 5 claims thành công
  - 95 nhận 409
  - Redis quota không âm
  - Inventory: 95 reservations được release qua CompensationOutbox
```

#### TC-CPN-005: Coupon fail → Inventory compensation thực sự chạy
```
Given: Coupon hết quota
When: Order request với coupon

Then:
  Step 1: Inventory reserved ✓
  Step 2: Coupon claim → 409
  Step 3: CompensationOutbox ghi IInventoryReleaseRequested
  Step 4: Worker publish event
  Step 5: Inventory consumer nhận event
  Step 6: Reservation released (Available tăng lại)

Verify sau 30 giây:
  - Reservation Status = RELEASED
  - Available = giá trị trước khi reserve
```

---

### 8.7 Save Order Failures

#### TC-SAVE-001: DB lỗi khi save order
```
Given: Simulate DB error khi INSERT Orders

Then:
  - 500
  - Orders: không có row
  - CompensationOutbox: có 2 rows (Inv + Coupon release)
  - Sau compensation:
    - Inventory.Available trả lại
    - CouponClaim.Status = RELEASED
    - Redis key xóa
```

#### TC-SAVE-002: Process crash sau reserve, trước save
```
Given: Kill process sau khi Inventory reserved và Coupon claimed

Then sau 15 phút:
  - TTL Job: Inventory reservation released tự động
  - TTL Job: Coupon claim released tự động
  - Redis coupon key expired

Client retry sau crash:
  - Cùng Idempotency-Key
  - Flow chạy lại bình thường
  - Không double reserve (IK derived)
```

---

### 8.8 TTL & Compensation Tests

#### TC-TTL-001: Inventory TTL auto-release
```
Given: Reservation với ExpiresAt = now - 1 phút

When: TTL Job chạy

Then:
  - Reservation.Status = RELEASED
  - Inventory.Available += quantity
  - Inventory.Reserved -= quantity
```

#### TC-TTL-002: Coupon TTL auto-release
```
Given: CouponClaim với Status=CLAIMED, ExpiresAt = now - 1 phút

When: TTL Job chạy

Then:
  - CouponClaim.Status = RELEASED
  - Redis key xóa
  - Coupon quota tăng lại
```

#### TC-TTL-003: Idempotency của TTL job
```
Given: TTL Job chạy 2 lần (overlap)

When: Cả 2 lần cùng process 1 reservation

Then:
  - Available không bị cộng 2 lần
  - Optimistic lock / status check ngăn double-release
```

#### TC-COMP-001: Duplicate compensation event
```
Given: Worker publish IInventoryReleaseRequested 2 lần (at-least-once)

When: Inventory Consumer nhận 2 lần

Then:
  - Lần 1: release thành công
  - Lần 2: check ProcessedEvents → skip
  - Available không bị cộng 2 lần
```

---

### 8.9 Idempotency Tests

#### TC-IK-001: Retry khi order đang xử lý (concurrent)
```
Given: Request đang xử lý (chưa có response)
When: Client gửi lại cùng IK
Then: Đợi request đầu tiên xong, trả cùng response (hoặc 409 Conflict tạm thời)
```

#### TC-IK-002: Retry sau crash — order không tồn tại
```
Given: Redis không có IK (process crashed)
When: Client retry với cùng IK
Then: Flow chạy lại bình thường từ đầu
```

#### TC-IK-003: Inventory idempotency
```
Given: Reserve request với IK đã tồn tại và reservation còn hiệu lực
When: Gọi reserve lại với cùng IK
Then: Trả reservationId cũ, không tạo reservation mới
```

---

## 9. Non-Functional Requirements

### 9.1 Performance Targets

| Metric | Target | Measurement |
|---|---|---|
| P50 latency (place order) | < 150ms | End-to-end, including DB + Redis |
| P95 latency | < 300ms | |
| P99 latency | < 500ms | |
| Throughput | 100 req/s sustained | 5 phút |
| Error rate (5xx) | < 0.1% | Under normal load |
| Circuit breaker recovery | < 15 giây | Sau khi dependency recover |

### 9.2 Monitoring Checklist

| Metric | Alert threshold |
|---|---|
| `order_place_total{status=5xx}` > 1% | Page |
| `order_place_duration_p99` > 1000ms | Warning |
| `compensation_triggered_total` tăng đột biến | Warning |
| `reservation_ttl_released_total` > bình thường ×3 | Info |
| `outbox_pending_count` > 100 | Warning |
| Circuit breaker OPEN | Page |

### 9.3 Logging Requirements

Mỗi request phải log:
- `correlationId` (xuyên suốt các service)
- `userId`
- `orderId` (sau khi tạo)
- Kết quả từng step (success/fail/latency)
- Reason khi reject

Log level:
- **ERROR:** 5xx responses, compensation triggered, circuit breaker opened
- **WARN:** Retry attempts, TTL releases, rate limit hits
- **INFO:** Order confirmed, compensation completed
- **DEBUG:** Từng validation step (chỉ dev/staging)