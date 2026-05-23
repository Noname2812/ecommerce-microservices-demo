# Auto Refund — Payment Service

Tự động tạo và xử lý refund cho 3 nhóm sự cố:

1. **Overpayment** — user trả dư > ngưỡng (default 10.000 VND) → refund phần dư.
2. **Cancelled / Expired but paid** — order saga đã cancel hoặc payment hết hạn nhưng webhook vẫn nhận tiền → refund toàn bộ.
3. **Timeout với partial payment** — bank transfer nhận một phần rồi hết hạn → refund phần đã nhận.

Cũng giải quyết race condition giữa `CancelPayment` và webhook nhờ Redis distributed lock.

## Component

| Component | File |
|---|---|
| Port | [IAutoRefundService.cs](../../src/Services/Payment/UrbanX.Payment.Application/Abstractions/IAutoRefundService.cs) |
| Impl | [AutoRefundService.cs](../../src/Services/Payment/UrbanX.Payment.Application/Services/AutoRefundService.cs) |
| Refund provider port | [IPaymentRefundProvider.cs](../../src/Services/Payment/UrbanX.Payment.Application/Abstractions/IPaymentRefundProvider.cs) |
| MoMo impl | [MomoRefundProvider.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Integrations/Momo/MomoRefundProvider.cs) |
| Options | [PaymentBusinessOptions.cs](../../src/Services/Payment/UrbanX.Payment.Application/Configuration/PaymentBusinessOptions.cs) |
| SEPay handler | [HandleSePayWebhookCommandHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/HandleSePayWebhook/HandleSePayWebhookCommandHandler.cs) |
| MoMo handler | [HandleMomoIpnCommandHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/HandleMomoIpn/HandleMomoIpnCommandHandler.cs) |
| Expire handler | [ExpirePaymentCommandHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/ExpirePayment/ExpirePaymentCommandHandler.cs) |

## Configuration

```jsonc
{
  "Payment": {
    "Business": {
      "OverpaymentRefundThresholdVnd": 10000   // delta dưới ngưỡng coi như "tip", không refund
    }
  }
}
```

Threshold chỉ áp dụng cho **overpayment**. Cancelled-but-paid và expiry-partial luôn refund toàn bộ (refund full amount, no threshold).

## Flow

```
AutoRefundService.CreateAndAttemptAsync(paymentId, amount, reason, enforceThreshold)
  │
  ├─ amount <= 0 → return null (no-op)
  ├─ enforceThreshold && amount <= threshold → return null + log
  ├─ Load Payment
  ├─ Create Refund(Pending) row
  ├─ Append PaymentEvent: AutoRefundCreated
  │
  ├─ Resolve IPaymentRefundProvider by Payment.ProviderName
  │     SePay → no provider → leave Pending (admin xử lý qua /complete API)
  │     MoMo  → MomoRefundProvider
  │
  ├─ provider.RefundAsync(refund.Id, paymentId, providerTransactionId, amount, reason)
  │     ↑
  │     MoMo orderId/requestId derived deterministically từ refund.Id
  │     → MoMo gateway dedup nếu retry → idempotent
  │
  ├─ Success → Refund.MarkCompleted(providerRefundId)
  │          → PaymentEvent: AutoRefundCompleted
  │          → Publish RefundProcessedV1 (outbox)
  │
  └─ Failure → Refund.MarkFailed()
              → PaymentEvent: AutoRefundFailed
              (Pending refund vẫn còn — admin có thể retry qua /complete)
```

## Khi nào auto-refund kích hoạt

### SEPay webhook (`HandleSePayWebhookCommandHandler`)

| Trigger | Reason | Threshold |
|---|---|---|
| `payment.Status == Cancelled` + transferAmount > 0 | `cancelled-but-paid:cancelled` | ❌ Refund toàn bộ |
| `payment.Status == Expired` (đã có) + transferAmount > 0 | `cancelled-but-paid:expired` | ❌ Refund toàn bộ |
| Mark Completed với delta > 0 (overpayment) | `overpayment-auto` | ✅ delta > 10k |

### MoMo IPN (`HandleMomoIpnCommandHandler`)

| Trigger | Reason | Threshold |
|---|---|---|
| `payment.Status == Cancelled` + resultCode success | `cancelled-but-paid:cancelled` | ❌ |
| `payment.Status == Expired` + resultCode success | `cancelled-but-paid:expired` | ❌ |
| Mark Completed với request.Amount > payment.Amount | `overpayment-auto` | ✅ delta > 10k |

### Expire job (`ExpirePaymentCommandHandler`)

| Trigger | Reason | Threshold |
|---|---|---|
| `payment.PaidAmount > 0` trước khi mark Expired | `expiry-partial-auto` | ❌ Refund toàn bộ partial |

## Concurrency: Cancel ↔ Webhook race

`CancelPaymentCommand` và `HandleSePayWebhookCommand` / `HandleMomoIpnCommand` đều dùng `[DistributedLock("payment:{PaymentId}")]` — Redis serialize hai thao tác.

**Two scenarios:**

1. **Webhook arrives BEFORE cancel** → payment đang PENDING → mark Completed → cancel handler thấy Completed → tạo refund qua existing `HandleOrderCancelledCommandHandler` (gọi `CreateRefundCommand`).

2. **Webhook arrives AFTER cancel** → payment đang Cancelled → webhook handler detect → tạo auto-refund cho transferAmount.

Cả hai đều dẫn về Refund(Pending) hoặc Completed tùy provider. Không có money loss.

## SEPay vs MoMo

| | SEPay | MoMo |
|---|---|---|
| Auto-refund creates Refund row | ✅ | ✅ |
| Provider API call | ❌ (không hỗ trợ) | ✅ |
| Final state | Pending → admin process qua `POST /payments/refunds/{id}/complete` (nhập providerRefundId tay) | Completed luôn (nếu MoMo trả `resultCode = 0`) |

Note: SEPay không có refund API. Auto-refund cho SEPay chỉ tạo row Pending + log event. Admin nhìn list refund (`GET /payments/{id}/refunds`) và xử lý ngoài banking.

## Idempotency

- MoMo refund: orderId = `refund-{refundId:N}`, requestId = `req-{refundId:N}`. Re-call cùng refundId → MoMo dedup, trả về cùng response (cho phép retry an toàn).
- Refund record có Id duy nhất tạo trước khi gọi provider — nếu DB commit thất bại sau khi provider API thành công thì transaction rollback, refund row chưa được insert. Lần retry sau sẽ tạo refund mới với refundId mới → MoMo dedup theo orderId (mới) → potential double-refund. Để hạn chế: chỉ retry khi network failure trước khi nhận response — pattern outbox cho refund event là cải tiến sau.

## Event types

Mới (PaymentEventTypes):
- `WEBHOOK_RECEIVED_AFTER_CANCELLATION`
- `AUTO_REFUND_CREATED`
- `AUTO_REFUND_COMPLETED`
- `AUTO_REFUND_FAILED`

Đã có:
- `WEBHOOK_RECEIVED_AFTER_EXPIRY`
- `WEBHOOK_OVERPAYMENT`
- `PAYMENT_EXPIRED`

## Non-goals

- Background reconciliation job pickup Pending refunds (đang inline trong handler).
- Manual override threshold per merchant.
- Webhook → outbox-based refund event pattern (hiện inline, transaction-coupled).
