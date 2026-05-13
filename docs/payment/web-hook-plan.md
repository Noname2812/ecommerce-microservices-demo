# Webhook Payment Flow — SePay
> UrbanX — Payment Service v1.3

---

## 1. Trạng thái Payment

```
Pending ──► PartiallyPaid ──► Completed
    │                │
    ▼                ▼
 Expired           Expired
```

> `Failed` được giữ trong enum nhưng **out of scope** cho plan này — không có transition nào dẫn đến `Failed` trong flow hiện tại.

| Status | Ý nghĩa |
|---|---|
| `Pending` | Chờ thanh toán |
| `PartiallyPaid` | Đã nhận được một phần tiền, chờ thêm |
| `Completed` | Đủ tiền, hoàn tất (terminal) |
| `Expired` | Quá hạn trước khi đủ tiền (terminal) |

---

## 2. Cấu trúc dữ liệu bổ sung

| Field | Mô tả |
|---|---|
| `payment.PaidAmount` | Tổng tiền đã nhận thực tế — **có thể vượt `Amount` trong trường hợp overpayment** (by design) |
| `payment.RemainingAmount` | `Amount - PaidAmount` (có thể âm khi overpayment) |
| `PaymentEvent.ExternalTransactionId` | SePay transaction ID của từng lần CK |
| `PaymentEvent.TransferAmount` | Số tiền của lần CK đó |

> **Invariant:** `PaidAmount >= Amount` khi `Status == Completed`. Các query/logic không được assume `PaidAmount <= Amount`.

---

## 3. Đảm bảo Concurrency & Transaction

### 3.1 Distributed Lock trên PaymentId

Command xử lý webhook phải khai báo:

```csharp
[DistributedLock("payment:{PaymentId}")]
public record HandleSePayWebhookCommand(..., Guid PaymentId) : ICommand<WebhookResult>;
```

`DistributedLockPipelineBehavior` (Shared.Messaging) sẽ acquire Redis lock trước khi handler chạy, đảm bảo:
- Hai partial webhook đến cùng lúc cho cùng `PaymentId` → chỉ 1 handler chạy tại một thời điểm, không mất cộng dồn `PaidAmount`
- Lock timeout → `Result.Failure(CacheErrors.LockTimeout(...))` → endpoint trả HTTP 503 (không phải 200 — webhook cần retry)

### 3.2 Race giữa webhook và background job (Expiry)

Background job cũng phải acquire cùng lock key `payment:{PaymentId}` trước khi transition sang `Expired`. Nếu không, có thể xảy ra:
- Webhook đọc `Pending` → background job đọc `Pending` → webhook ghi `PartiallyPaid` → background job ghi `Expired` (overwrite)

**Fix:** Background job dùng `IDistributedLockService.AcquireAsync("payment:{id}")` cho từng payment trước khi update status.

### 3.3 Transaction boundary

Toàn bộ bước 3–8 trong handler phải nằm trong **một transaction duy nhất**:
- Dedup check (`ExistsAsync`) + insert `PaymentEvent` + update `Payment` + ghi Outbox
- `TransactionPipelineBehavior` tự wrap nếu dùng Command qua MediatR — handler **không được** gọi `SaveChanges` thủ công

### 3.4 Dedup strategy

Dùng partial unique index, **không** dùng `IIdempotentCommand`:
```sql
CREATE UNIQUE INDEX idx_payment_event_ext_tx_id
  ON payment_events(external_transaction_id)
  WHERE external_transaction_id IS NOT NULL;
```

`IIdempotentCommand` dùng client-generated `RequestId` — không phù hợp ở đây vì key dedup là SePay's transaction ID.

---

## 4. Webhook handler flow

### Bước 1 — Verify token (tại endpoint, KHÔNG trong handler)

Thực hiện trong Carter endpoint bằng `IEndpointFilter` trước khi gọi `ISender.Send(...)`:

```csharp
v1.MapPost("/webhook/sepay", HandleWebhook)
  .AddEndpointFilter<SePayWebhookAuthFilter>();
```

- `Authorization != "Bearer {WebhookSecret}"` → **HTTP 401**, dừng — không vào handler
- Xử lý sớm ở boundary, tránh MediatR pipeline chạy cho request không hợp lệ

### Bước 2 — Skip tiền ra (trong handler)

- `TransferType != "in"` → return `{ success: true }` — HTTP 200

### Bước 3 — Dedup

- `PaymentEvent.ExistsAsync(ExternalTransactionId)` → nếu đã tồn tại: return `{ success: true }` *(duplicate webhook)*
- Partial unique index là safety net — check in-memory trước để tránh exception trên DB

### Bước 4 — Match payment

- `ILike(content, "%{OrderNumber}%")` narrow ở DB → verify chính xác in-memory bằng Regex
- Không tìm thấy → log warn (để audit) → return `{ success: true, message: "no match" }` — HTTP 200

### Bước 5 — Phân nhánh theo `payment.Status`

```
payment.Status == Completed  →  return { success: true, message: "already completed" }
payment.Status == Expired    →  LATE PAYMENT flow  (xem mục 5.4)
payment.Status == Pending
  hoặc PartiallyPaid         →  tiếp tục bước 6
```

### Bước 6 — Tính toán amount

```
newPaidAmount  = payment.PaidAmount + TransferAmount
delta          = newPaidAmount - payment.Amount   (âm = thiếu, dương = dư)
```

### Bước 7 — Phân nhánh theo amount

```
delta < 0   →  PARTIAL PAYMENT flow     (xem mục 5.1)
delta == 0  →  COMPLETE flow            (xem mục 5.2)
delta > 0   →  OVERPAYMENT flow         (xem mục 5.3)
```

### Bước 8 — Lưu PaymentEvent + Outbox (trong cùng transaction — mọi nhánh)

- `PaymentEvent { ExternalTransactionId, TransferAmount, EventType, Source=WebhookSepay, Payload }`
- Nếu payment đã `Completed`: publish `PaymentCompletedV1` qua Outbox

---

## 5. Chi tiết từng scenario

### 5.1 Partial payment & Multiple transfers

**Điều kiện:** `delta < 0` (`newPaidAmount < payment.Amount`)

**Flow:**
1. `payment.PaidAmount += TransferAmount`
2. `payment.Status = PartiallyPaid` (nếu đang `Pending`; nếu đã `PartiallyPaid` giữ nguyên)
3. Ghi event `WEBHOOK_PARTIAL_RECEIVED` với `{ transferAmount, paidAmount, remainingAmount }`
4. **Không** publish Outbox event — chờ lần CK tiếp theo

**Lần CK tiếp theo** vào cùng flow, cộng dồn `PaidAmount` cho đến khi đủ.

> `PartiallyPaid` vẫn có `ExpiresAt` — background job sẽ chuyển sang `Expired` nếu hết giờ (xem mục 6).

---

### 5.2 Exact payment — Complete

**Điều kiện:** `delta == 0` (`newPaidAmount == payment.Amount`)

**Flow:**
1. `payment.PaidAmount = payment.Amount`
2. `payment.Status = Completed`
3. `payment.ProviderResponse = JSON(payload)`
4. Ghi event `WEBHOOK_RECEIVED`
5. Publish `PaymentCompletedV1 { PaymentId, OrderId, Amount, PaidAmount }` qua Outbox

---

### 5.3 Overpayment

**Điều kiện:** `delta > 0` (`newPaidAmount > payment.Amount`)

**Flow:**
1. `payment.PaidAmount = newPaidAmount` *(ghi lại số thực tế — có thể > `Amount`)*
2. `payment.Status = Completed`
3. `payment.ProviderResponse = JSON(payload)`
4. Ghi event `WEBHOOK_RECEIVED`
5. Ghi thêm event `WEBHOOK_OVERPAYMENT` với `{ delta, paidAmount, expectedAmount }` — để kế toán reconcile
6. Publish `PaymentCompletedV1 { PaymentId, OrderId, Amount, PaidAmount }` qua Outbox — `PaidAmount > Amount` là dấu hiệu cần reconcile

> Không tự động refund — kế toán xử lý thủ công dựa vào event `WEBHOOK_OVERPAYMENT`.

---

### 5.4 Late payment

**Điều kiện:** `payment.Status == Expired`

**Flow:**
1. Ghi event `WEBHOOK_RECEIVED_AFTER_EXPIRY` với `{ transferAmount, expiredAt, paidBeforeExpiry }`
2. **Không** complete payment — trạng thái giữ nguyên `Expired`
3. **Không** publish Outbox event
4. Return `{ success: true }` — tiền đã vào tài khoản nhưng cần xử lý thủ công

> Refund flow nằm ngoài scope. Event `WEBHOOK_RECEIVED_AFTER_EXPIRY` là trigger để ops team xử lý.

---

### 5.5 Duplicate webhook

**Điều kiện:** `ExistsAsync(ExternalTransactionId) == true` *(bước 3)*

Return ngay `{ success: true }` — không ghi event, không thay đổi state.

> Safety net: partial unique index trên `payment_events(external_transaction_id)` đảm bảo chỉ 1 insert thành công kể cả khi check in-memory bị bypass.

---

## 6. Background job — Expiry

```
payment.Status IN (Pending, PartiallyPaid)
AND NOW() > payment.ExpiresAt
```

Với mỗi payment match, job phải:
1. **Acquire distributed lock** `payment:{id}` (dùng `IDistributedLockService.AcquireAsync`) — tránh race với webhook handler
2. Re-fetch payment sau khi có lock — nếu status đã thay đổi (webhook vừa complete), bỏ qua
3. `payment.Status = Expired`
4. Ghi event `PAYMENT_EXPIRED { paidAmount, remainingAmount }`
5. Publish `PaymentExpiredV1` qua Outbox (notify Order Service)
6. Release lock

---

## 7. Event types

| EventType | Trigger | Outbox |
|---|---|---|
| `WEBHOOK_PARTIAL_RECEIVED` | CK thiếu, cộng dồn vào `PaidAmount` | Không |
| `WEBHOOK_RECEIVED` | Đủ/dư tiền, complete | **Có** — `PaymentCompletedV1` |
| `WEBHOOK_OVERPAYMENT` | CK dư (đi kèm `WEBHOOK_RECEIVED`) | Không (đi kèm `PaymentCompletedV1`) |
| `WEBHOOK_RECEIVED_AFTER_EXPIRY` | Webhook đến sau khi `Expired` | Không |
| `PAYMENT_EXPIRED` | Background job expire | **Có** — `PaymentExpiredV1` |

---

## 8. Verification

| Test case | Expected |
|---|---|
| CK đúng số tiền 1 lần | `Completed`, event `WEBHOOK_RECEIVED`, `PaymentCompletedV1` trong Outbox |
| CK lần 1 thiếu | `PartiallyPaid`, event `WEBHOOK_PARTIAL_RECEIVED`, không Outbox |
| CK lần 2 đủ (cộng dồn) | `Completed`, event `WEBHOOK_RECEIVED`, `PaymentCompletedV1` trong Outbox |
| CK 3 lần nhỏ, tổng đủ | `Completed` sau lần 3, 2 `WEBHOOK_PARTIAL_RECEIVED` + 1 `WEBHOOK_RECEIVED` |
| CK dư tiền | `Completed`, `WEBHOOK_OVERPAYMENT` với delta, `PaidAmount > Amount` trong Outbox event |
| CK sau khi Expired | `Expired` giữ nguyên, event `WEBHOOK_RECEIVED_AFTER_EXPIRY` |
| Gửi lại cùng webhook 2 lần | Idempotent — state không đổi, không event mới |
| 2 webhook partial cùng PaymentId đồng thời | Distributed lock — 1 handler đợi, PaidAmount cộng dồn đúng |
| Webhook + background job expiry đồng thời | Lock trên PaymentId — re-fetch sau lock đảm bảo không overwrite |
| `PartiallyPaid` hết giờ trước khi đủ tiền | Background job (với lock + re-fetch) → `Expired`, `PAYMENT_EXPIRED` |
| Token sai | HTTP 401 tại endpoint filter, không vào MediatR pipeline |
| Lock timeout (Redis không available) | HTTP 503, SePay retry sau |
