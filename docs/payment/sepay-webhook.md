# SePay Webhook — Payment Service

> Payment Service v1.3 · SePay bank-transfer flow

---

## 1. Tổng quan

Payment Service nhận webhook từ SePay khi có chuyển khoản vào tài khoản ngân hàng. Flow hỗ trợ:

- Thanh toán đúng số tiền (1 lần)
- Thanh toán thiếu — nhiều lần CK cộng dồn (`PartiallyPaid`)
- Thanh toán dư (overpayment)
- CK đến sau khi payment đã hết hạn (late payment)
- Dedup webhook (gửi lại 2 lần cùng transaction)
- Concurrency: 2 webhook đến cùng lúc cho cùng payment

---

## 2. Payment Status

```
Pending ──► PartiallyPaid ──► Completed
   │               │
   ▼               ▼
Expired         Expired
```

| Status | Mô tả | Terminal |
|---|---|---|
| `PENDING` | Chờ thanh toán | |
| `PARTIALLY_PAID` | Đã nhận một phần, chờ thêm | |
| `COMPLETED` | Đủ/dư tiền, hoàn tất | ✓ |
| `EXPIRED` | Hết hạn trước khi đủ tiền | ✓ |
| `PROCESSING` | Đang xử lý qua payment gateway (non-SePay) | |
| `FAILED` | Thất bại (non-SePay flow) | ✓ |
| `CANCELLED` | Đã huỷ | ✓ |

> `CancelPayment` cho phép huỷ từ `Pending`, `Processing`, hoặc `PartiallyPaid`.

---

## 3. Endpoint

```
POST /api/v1/payments/webhook/sepay
Authorization: Bearer <WebhookSecret>
Content-Type: application/json
```

**Request body (SePay format):**

```json
{
  "id": 123456789,
  "content": "CK don hang ORD-2025-001",
  "transferAmount": 250000,
  "transferType": "in"
}
```

**Response (mọi trường hợp):**

```json
{ "success": true }           // xử lý thành công hoặc skipped
{ "success": true, "message": "already completed" }
{ "success": true, "message": "no match" }
{ "success": false }          // lỗi parse JSON / body rỗng
```

> SePay yêu cầu response `{ success: true }` để dừng retry. HTTP 401 (sai secret) hoặc HTTP 503 (lock timeout) sẽ bị SePay retry.

---

## 4. Authentication

`SePayWebhookAuthFilter` (`IEndpointFilter`) kiểm tra header trước khi vào MediatR pipeline:

```
Authorization: Bearer <SePay:WebhookSecret>
```

- Thiếu / sai → **HTTP 401** — không vào handler
- Secret rỗng (chưa cấu hình) → **HTTP 503**

---

## 5. Webhook Handler Flow

### Bước 1 — Resolve payment từ nội dung CK

`ResolveSePayWebhookPaymentQuery(content)` query DB với `ILike` rồi xác nhận bằng Regex:

```sql
WHERE @content ILIKE '%' || "OrderNumber" || '%'
LIMIT <WebhookMemoMatchCandidateLimit>
```

Nếu ILike trả nhiều kết quả, hàm `TryResolveSinglePaymentId` ưu tiên theo thứ tự:

1. Một payment `Pending`/`PartiallyPaid` (open)
2. Một payment `Expired` (late transfer)
3. Một payment `Processing`
4. Một payment `Completed` (idempotent)

Ambiguous (nhiều payment cùng loại) → "no match", log warn.

### Bước 2 — HandleSePayWebhookCommand (có Distributed Lock)

Command được khoá bằng `[DistributedLock("payment:{PaymentId}")]` — đảm bảo chỉ 1 handler chạy tại một thời điểm cho cùng `PaymentId`.

| Bước | Điều kiện | Hành động |
|---|---|---|
| Skip | `TransferType != "in"` | return `{ success: true }` |
| Dedup | `ExternalTransactionId` đã tồn tại | return `{ success: true }` |
| Not found | Payment không tồn tại | log warn, return `{ success: true }` |
| Regex re-check | OrderNumber không khớp content (sau lock) | log warn, skip |
| Already done | `Status == Completed` | return `{ success: true, message: "already completed" }` |
| Late payment | `Status == Expired` | ghi event `WEBHOOK_RECEIVED_AFTER_EXPIRY` |
| Partial | `delta < 0` | `MarkPartiallyPaid`, ghi event `WEBHOOK_PARTIAL_RECEIVED` |
| Exact / Over | `delta >= 0` | `MarkCompletedViaBankTransfer`, ghi `WEBHOOK_RECEIVED` (+ `WEBHOOK_OVERPAYMENT` nếu dư), publish Outbox |

---

## 6. Scenarios chi tiết

### 6.1 Partial payment

- `payment.PaidAmount += TransferAmount`
- `payment.RemainingAmount = Amount - PaidAmount`
- `Status → PartiallyPaid`
- Event: `WEBHOOK_PARTIAL_RECEIVED`
- **Không** publish Outbox — chờ lần CK tiếp theo

### 6.2 Exact payment

- `payment.PaidAmount = Amount`
- `payment.RemainingAmount = 0`
- `Status → Completed`
- Event: `WEBHOOK_RECEIVED`
- Publish: `PaymentCompletedV1`

### 6.3 Overpayment

- `payment.PaidAmount = newPaidAmount` (> `Amount`)
- `payment.RemainingAmount` âm (by design)
- `Status → Completed`
- Event: `WEBHOOK_RECEIVED` + `WEBHOOK_OVERPAYMENT { delta, paidAmount, expectedAmount }`
- Publish: `PaymentCompletedV1` với `PaidAmount > Amount` — dấu hiệu để kế toán reconcile
- **Không tự động refund**

### 6.4 Late payment (sau Expired)

- `Status` giữ nguyên `Expired`
- Event: `WEBHOOK_RECEIVED_AFTER_EXPIRY { transferAmount, expiredAt, paidBeforeExpiry }`
- **Không** publish Outbox — ops team xử lý thủ công

### 6.5 Duplicate webhook

- Dedup bằng `ExternalTransactionId` (check in-memory trước, partial unique index là safety net)
- return `{ success: true }` — không ghi event, không thay đổi state

---

## 7. Concurrency & Transaction

### Distributed Lock

`HandleSePayWebhookCommand` và `ExpirePaymentCommand` đều dùng lock key `payment:{PaymentId}`:

- 2 webhook cùng lúc → 1 handler đợi lock → PaidAmount cộng dồn đúng
- Lock timeout (Redis down) → `CacheErrors.LockTimeout` → **HTTP 503** → SePay retry

### Transaction boundary

`TransactionPipelineBehavior` wrap toàn bộ handler trong 1 transaction:

- Dedup check + insert `PaymentEvent` + update `Payment` + ghi Outbox — atomic
- Handler **không** gọi `SaveChanges` thủ công

### Partial unique index (dedup safety net)

```sql
CREATE UNIQUE INDEX idx_payment_event_ext_tx_id
  ON payment_events("ExternalTransactionId")
  WHERE "ExternalTransactionId" IS NOT NULL;
```

---

## 8. Background Job — Expiry Sweep

`PaymentExpirySweepHostedService` chạy định kỳ (cấu hình `ExpirySweepIntervalSeconds`):

1. `SweepExpiredPaymentsCommand` lấy tối đa `ExpirySweepBatchSize` ID có `Status IN (Pending, PartiallyPaid) AND ExpiresAt < NOW()`
2. Với mỗi ID, tạo scope riêng và gửi `ExpirePaymentCommand(id)` — mỗi payment có transaction + distributed lock độc lập
3. `ExpirePaymentCommand` re-fetch sau khi có lock — nếu status đã thay đổi (webhook vừa complete), bỏ qua
4. `Status → Expired`, ghi event `PAYMENT_EXPIRED`, publish `PaymentExpiredV1`

> Scope riêng per payment tránh nested transaction và đảm bảo mỗi expiry là atomic độc lập.

---

## 9. Integration Events

| Event | Trigger | Kênh |
|---|---|---|
| `PaymentCompletedV1` | CK đủ/dư, `Status → Completed` | Outbox → RabbitMQ |
| `PaymentExpiredV1` | Background job expire | Outbox → RabbitMQ |

**`PaymentCompletedV1` fields:**

```csharp
Guid PaymentId, Guid OrderId, string OrderNumber, Guid CustomerId,
decimal Amount, decimal PaidAmount,   // PaidAmount > Amount = overpayment
string Currency, string ProviderName, string? ProviderTransactionId,
DateTimeOffset PaidAt
```

**`PaymentExpiredV1` fields:**

```csharp
Guid PaymentId, Guid OrderId, string OrderNumber, Guid CustomerId,
decimal Amount, decimal PaidAmount, decimal RemainingAmount,
DateTimeOffset ExpiredAt
```

---

## 10. Event Types (PaymentEvent table)

| EventType | Trigger | Outbox |
|---|---|---|
| `WEBHOOK_PARTIAL_RECEIVED` | CK thiếu | Không |
| `WEBHOOK_RECEIVED` | CK đủ/dư, complete | Có — `PaymentCompletedV1` |
| `WEBHOOK_OVERPAYMENT` | CK dư (đi kèm `WEBHOOK_RECEIVED`) | Không |
| `WEBHOOK_RECEIVED_AFTER_EXPIRY` | CK sau khi Expired | Không |
| `PAYMENT_EXPIRED` | Background job | Có — `PaymentExpiredV1` |

---

## 11. Configuration (`appsettings.json` section `SePay`)

| Key | Default | Mô tả |
|---|---|---|
| `WebhookSecret` | `""` | Secret khớp với SePay dashboard. **Bắt buộc set** — để trống → 503 |
| `PaymentExpiresAfterHours` | `72` | Thời gian hết hạn payment (giờ) |
| `PaymentExpiresAfterHoursMinimum` | `1` | Giá trị tối thiểu hợp lệ cho `PaymentExpiresAfterHours` |
| `PaymentExpiresAfterHoursFallback` | `72` | Dùng khi `PaymentExpiresAfterHours < Minimum` |
| `WebhookMemoMatchCandidateLimit` | `50` | Số rows tối đa từ ILike query trước khi Regex lọc |
| `ExpirySweepBatchSize` | `200` | Số payments tối đa xử lý mỗi lần sweep |
| `ExpirySweepInitialDelaySeconds` | `5` | Delay trước lần sweep đầu tiên sau khởi động |
| `ExpirySweepIntervalSeconds` | `60` | Khoảng thời gian giữa các lần sweep |
| `ExpirySweepMinimumIntervalSeconds` | `10` | Floor cho interval (dùng `Math.Max`) |

---

## 12. Key Files

| File | Vai trò |
|---|---|
| `API/Apis/SePayWebhookApis.cs` | Carter endpoint, deserialize DTO, gọi resolve + handle command |
| `API/Filters/SePayWebhookAuthFilter.cs` | Verify `Authorization: Bearer` token |
| `API/BackgroundJobs/PaymentExpirySweepHostedService.cs` | Background sweep loop |
| `Application/Usecases/V1/Command/HandleSePayWebhook/` | Command + Validator + Handler |
| `Application/Usecases/V1/Command/ExpirePayment/` | Expire 1 payment (có distributed lock) |
| `Application/Usecases/V1/Command/SweepExpiredPayments/` | Batch expire — gọi ExpirePayment per-scope |
| `Application/Usecases/V1/Query/ResolveSePayWebhookPayment/` | Match payment từ transfer content |
| `Application/Configuration/SePayOptions.cs` | Config options |
| `Domain/Models/Payment.cs` | Entity với `MarkPartiallyPaid`, `MarkCompletedViaBankTransfer` |
| `Domain/ValueObjects/PaymentEventTypes.cs` | Hằng số event type string |
| `Persistence/Configurations/PaymentEventConfiguration.cs` | Partial unique index trên `ExternalTransactionId` |
