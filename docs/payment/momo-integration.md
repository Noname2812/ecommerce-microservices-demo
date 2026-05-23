# MoMo Capture Wallet — Payment Service

Tích hợp MoMo Capture Wallet song song SEPay. User chọn phương thức thanh toán (`Sepay` / `Momo`) tại UI checkout — field carry qua Saga → Payment service dispatch tới đúng provider.

## Flow tổng quan

```
[Order Saga]                              [Payment Service]                      [MoMo Gateway]            [User]
     │                                          │                                     │                       │
     ├── publish CreatePaymentSessionV1 ───────►│  (PaymentMethod=Momo)               │                       │
     │                                          ├─ CreatePaymentSessionCmd            │                       │
     │                                          ├─ Resolve IPaymentSessionProvider    │                       │
     │                                          ├─ MomoPaymentProvider                │                       │
     │                                          ├─ Build sig + POST /v2/.../create  ─►│                       │
     │                                          │◄── { resultCode:0, payUrl, … } ─────┤                       │
     │                                          ├─ Save Payment(PENDING) + PayUrl     │                       │
     │   PaymentSessionCreatedV1 ◄──────────────┤  (PaymentUrl=payUrl)                │                       │
     │                                          │                                     │  ─── redirect ───────►│
     │                                          │                                     │◄── user pays ─────────┤
     │                                          │  POST /webhook/momo (IPN) ◄─────────┤                       │
     │                                          ├─ Verify HMAC-SHA256 signature       │                       │
     │                                          ├─ HandleMomoIpnCommand               │                       │
     │                                          ├─ Lookup Payment by orderId          │                       │
     │                                          ├─ MarkCompleted / Failed             │                       │
     │   ◄── PaymentCompletedV1 ─────────────────┤  (via MT EF Outbox)                │                       │
     │                                          │                                     │                       │
```

| Component | File |
|---|---|
| Webhook (IPN) endpoint | [MomoWebhookApis.cs](../../src/Services/Payment/UrbanX.Payment.API/Apis/MomoWebhookApis.cs) |
| IPN handler | [HandleMomoIpnCommandHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/HandleMomoIpn/HandleMomoIpnCommandHandler.cs) |
| Session provider | [MomoPaymentProvider.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Integrations/Momo/MomoPaymentProvider.cs) |
| Refund provider | [MomoRefundProvider.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Integrations/Momo/MomoRefundProvider.cs) |
| HTTP client | [MomoClient.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Integrations/Momo/MomoClient.cs) |
| Signature helper | [MomoSignature.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Integrations/Momo/MomoSignature.cs) |
| Options + Validator | [MomoOptions.cs](../../src/Services/Payment/UrbanX.Payment.Application/Configuration/MomoOptions.cs) · [MomoOptionsValidator.cs](../../src/Services/Payment/UrbanX.Payment.Application/Configuration/MomoOptionsValidator.cs) |
| Enum `PaymentMethod` | [PaymentMethod.cs](../../src/Shared/Shared.Contract/Dtos/Payment/PaymentMethod.cs) |

## Cấu hình

Section `Momo` trong `appsettings.json`:

```jsonc
{
  "Momo": {
    "PartnerCode": "MOMO",
    "AccessKey":   "F8BBA842ECF85",
    "SecretKey":   "K951B6PE1waDMi640xX08PD3vg6EkVlz",
    "Endpoint":    "https://test-payment.momo.vn",
    "IpnUrl":      "https://YOUR_PUBLIC_HOST/api/v1/payments/webhook/momo",
    "RedirectUrl": "http://localhost:5173/checkout/result",
    "Lang":        "vi",
    "RequestExpireSeconds": 1800,
    "TimeoutSeconds":       30
  }
}
```

| Key | Bắt buộc | Mô tả |
|---|---|---|
| `PartnerCode` | ✅ | Sandbox dùng `MOMO`. Prod là mã merchant cấp riêng |
| `AccessKey` / `SecretKey` | ✅ | Sandbox shared `F8BBA842ECF85` / `K951B6PE1waDMi640xX08PD3vg6EkVlz`. Prod nạp qua user-secrets / env var |
| `Endpoint` | ✅ | Sandbox `https://test-payment.momo.vn` · Prod `https://payment.momo.vn` |
| `IpnUrl` | ✅ | URL **public** MoMo POST IPN về (phải có thể truy cập từ internet) |
| `RedirectUrl` | ✅ | URL frontend nhận user sau khi pay |
| `Lang` | ⛔ | `vi` (default) / `en` |
| `RequestExpireSeconds` | ⛔ | TTL session (mặc định 30 phút) — set `Payment.ExpiresAt` |
| `TimeoutSeconds` | ⛔ | HTTP timeout với MoMo gateway (>= 5s) |

**Dev secrets** — không commit:
```bash
cd src/Services/Payment/UrbanX.Payment.API
dotnet user-secrets init
dotnet user-secrets set "Momo:SecretKey" "<your-secret>"
dotnet user-secrets set "Momo:AccessKey" "<your-access>"
```

## Signature spec

HMAC-SHA256 hex (lowercase) sang chuỗi canonical: các field sort alphabetical theo key, nối `key=value` bằng `&` (không URL-encode).

### `/create`
```
accessKey={accessKey}&amount={amount}&extraData={extraData}&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}
```

### IPN (server xác thực)
```
accessKey={accessKey}&amount={amount}&extraData={extraData}&message={message}&orderId={orderId}&orderInfo={orderInfo}&orderType={orderType}&partnerCode={partnerCode}&payType={payType}&requestId={requestId}&responseTime={responseTime}&resultCode={resultCode}&transId={transId}
```

### `/refund`
```
accessKey={accessKey}&amount={amount}&description={description}&orderId={orderId}&partnerCode={partnerCode}&requestId={requestId}&transId={transId}
```

## Test IPN bằng curl (giả lập)

```bash
ACCESS=F8BBA842ECF85
SECRET=K951B6PE1waDMi640xX08PD3vg6EkVlz
ORDER=UX-abc123def456                       # = Payment.TransferReference đã lưu khi tạo session
REQ=req-$RANDOM
AMT=150000
TRANS=$RANDOM$RANDOM                          # transId của MoMo
RC=0                                          # 0=success, 1006=user denied, ...
RT=$(date +%s%3N)

RAW="accessKey=$ACCESS&amount=$AMT&extraData=&message=Successful.&orderId=$ORDER&orderInfo=UX&orderType=momo_wallet&partnerCode=MOMO&payType=qr&requestId=$REQ&responseTime=$RT&resultCode=$RC&transId=$TRANS"
SIG=$(printf '%s' "$RAW" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $NF}')

curl -X POST http://localhost:5015/api/v1/payments/webhook/momo \
  -H "Content-Type: application/json" \
  -d "{\"partnerCode\":\"MOMO\",\"orderId\":\"$ORDER\",\"requestId\":\"$REQ\",\"amount\":$AMT,\"transId\":$TRANS,\"resultCode\":$RC,\"message\":\"Successful.\",\"orderInfo\":\"UX\",\"orderType\":\"momo_wallet\",\"payType\":\"qr\",\"responseTime\":$RT,\"extraData\":\"\",\"signature\":\"$SIG\"}"
```

Server trả `{"resultCode":0,"message":"Confirm Success"}` khi handler chạy xong (kể cả khi không match → vẫn 200 để MoMo dừng retry).

## resultCode quan trọng

| Code | Nghĩa | Final? | Hành xử của handler |
|---|---|---|---|
| `0` | Success | ✅ | `MarkCompletedViaBankTransfer` → publish `PaymentCompletedV1` |
| `9000` | Authorized (2-step) | ✅ | Coi như success |
| `1000` / `7000` / `7002` | Pending | ❌ | Record event, giữ PENDING |
| `1001` / `1002` / `1003` / `1006` / `1017` | Fail | ✅ | `MarkFailed` → publish `PaymentFailedV1` |
| Khác | Fail | ✅ | Same as fail |

## Edge cases

| Tình huống | Handler hành xử |
|---|---|
| IPN trùng (cùng `transId`) | `PaymentEventRepository.ExistsByExternalTransactionIdAsync` dedup → return Success |
| Signature sai | Endpoint return 204 (silent ignore — tránh retry loop) |
| `orderId` không match Payment | Return 200 (acknowledge) để MoMo dừng retry |
| Payment status `Completed` | Return 200 với message `already completed` |
| Payment status `Expired` + resultCode success | Record `WebhookReceivedAfterExpiry` event + **auto refund full amount** (no threshold). Xem `IAutoRefundService` |
| Payment status `Cancelled` + resultCode success | Record `WebhookReceivedAfterCancellation` event + **auto refund full amount**. Race condition order-cancel ↔ webhook |
| `resultCode` pending (1000/7000) | Ghi event nhưng giữ PENDING — IPN sau sẽ finalize |
| Overpayment (request.Amount > payment.Amount, delta > 10k VND) | Mark Completed bình thường + record `WebhookOverpayment` + **auto refund excess delta** |

## Refund auto qua MoMo API

Khi `CompleteRefundCommand` được dispatch trên payment có `ProviderName = "MoMo"`:
1. Handler load Payment + Refund.
2. Resolve `IPaymentRefundProvider` theo `payment.ProviderName` → `MomoRefundProvider`.
3. Build signature `/refund` → POST tới MoMo gateway.
4. Nếu `resultCode = 0` → lấy `transId` làm `providerRefundId`, set Refund.Status = Completed, publish `RefundProcessedV1`.
5. Nếu fail → `MarkFailed`, trả `PaymentErrors.RefundFailed`.

SEPay vẫn refund thủ công (caller truyền `ProviderRefundId` trực tiếp) — không có `IPaymentRefundProvider` SEPay nên handler skip nhánh provider auto.

## Frontend contract

`POST /api/v1/orders/place-order-normal` (và `place-sales-order`):
```json
{
  "shippingAddress": { ... },
  "shippingFee": 30000,
  "couponCode": null,
  "idempotencyKey": "01928374-1234-1234-1234-abcdef012345",
  "pricingSnapshot": { ... },
  "items": [ ... ],
  "customerEmail": "user@example.com",
  "paymentMethod": "Momo"      // <-- "Sepay" | "Momo" (case-sensitive enum, default "Sepay")
}
```

Response từ saga (qua `PaymentSessionCreatedV1`):
- MOMO: `PaymentUrl = payUrl` (redirect browser tới link MoMo)
- SEPAY: `PaymentUrl = QrCodeUrl` (render QR ảnh tĩnh)

## Migration

```bash
# Payment
dotnet ef migrations add AddPayUrlAndMomoProvider \
  --project src/Services/Payment/UrbanX.Payment.Persistence \
  --startup-project src/Services/Payment/UrbanX.Payment.API

# Order
dotnet ef migrations add AddPaymentMethodToSaga \
  --project src/Services/Order/UrbanX.Order.Persistence \
  --startup-project src/Services/Order/UrbanX.Order.API
```

Migration đã có sẵn:
- `20260523140835_AddPayUrlAndMomoProvider` — thêm `pay_url`, resize `transfer_reference`, seed MoMo provider row
- `20260523140634_AddPaymentMethodToSaga` — thêm column `payment_method` vào hai bảng saga (default `Sepay`)

## Non-goals

- MoMo All-in-One / ATM / Credit card (chỉ Capture Wallet)
- Background reconciliation job với MoMo `/query` (chỉ rely on IPN)
- IP allowlist / mTLS với MoMo (Cloudflare/WAF lo)
- Frontend UI chọn method — chỉ document API contract; FE tự update
