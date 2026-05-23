# SEPay Integration — Payment Service

Bank-transfer payment via SEPay (VietQR) cho UrbanX. Kết hợp QR tĩnh trên qr.sepay.vn + webhook HMAC-SHA256 để nhận thông báo chuyển khoản.

## Flow tổng quan

```
[Order Saga]                            [Payment Service]                    [SEPay]                [Bank]
     │                                          │                              │                     │
     ├── publish CreatePaymentSessionV1 ───────►│                              │                     │
     │                                          ├─ CreatePaymentSessionCmd     │                     │
     │                                          ├─ Tạo Payment (PENDING)       │                     │
     │                                          ├─ Sinh QR URL static          │                     │
     │   PaymentSessionCreatedV1 ◄──────────────┤  (qr.sepay.vn/img?…)         │                     │
     │   (PaymentUrl=QrCodeUrl)                 │                              │                     │
     │                                          │                              │                     │
     │   [User scan QR → CK qua app banking]                                   │                     │
     │                                          │                              │  ◄── credit ────────┤
     │                                          │                              │  (memo có Order#)   │
     │                                          │  POST /webhook/sepay ◄───────┤                     │
     │                                          ├─ SePayWebhookAuthFilter      │                     │
     │                                          │   verify HMAC-SHA256         │                     │
     │                                          ├─ ResolveSePayWebhookPayment  │                     │
     │                                          ├─ HandleSePayWebhookCommand   │                     │
     │                                          ├─ MarkCompleted / Partial     │                     │
     │   ◄── PaymentCompletedV1 ────────────────┤  (via MT EF Outbox)          │                     │
     │                                          │                              │                     │
```

Component                      | File
---                            | ---
Webhook endpoint               | [SePayWebhookApis.cs](../../src/Services/Payment/UrbanX.Payment.API/Apis/SePayWebhookApis.cs)
HMAC verification filter       | [SePayWebhookAuthFilter.cs](../../src/Services/Payment/UrbanX.Payment.API/Filters/SePayWebhookAuthFilter.cs)
Create session consumer        | [CreatePaymentSessionConsumer.cs](../../src/Services/Payment/UrbanX.Payment.Infrastructure/Messaging/CreatePaymentSession/CreatePaymentSessionConsumer.cs)
Create session command         | [CreatePaymentSessionCommand.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/CreatePaymentSession/CreatePaymentSessionCommand.cs)
Webhook handler                | [HandleSePayWebhookCommandHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Command/HandleSePayWebhook/HandleSePayWebhookCommandHandler.cs)
Memo → PaymentId resolver      | [ResolveSePayWebhookPaymentQueryHandler.cs](../../src/Services/Payment/UrbanX.Payment.Application/Usecases/V1/Query/ResolveSePayWebhookPayment/ResolveSePayWebhookPaymentQueryHandler.cs)
Options                        | [SePayOptions.cs](../../src/Services/Payment/UrbanX.Payment.Application/Configuration/SePayOptions.cs) + [Validator](../../src/Services/Payment/UrbanX.Payment.Application/Configuration/SePayOptionsValidator.cs)

## Cấu hình

Section `SePay` trong `appsettings.json`:

```json
{
  "SePay": {
    "BankAccount": "0123456789",
    "BankCode": "MB",
    "AccountHolderName": "URBANX SHOP",
    "QrTemplate": "compact",
    "HmacSecret": "<secret-32+ char>",
    "WebhookTimestampToleranceSeconds": 300,
    "PaymentSessionExpiresAfterMinutes": 30,
    "PaymentExpiresAfterHours": 72
  }
}
```

Key                                   | Bắt buộc | Mô tả
---                                   | ---     | ---
`BankAccount`                         | ✅      | Số tài khoản nhận tiền (xuất hiện trong QR)
`BankCode`                            | ✅      | Mã ngân hàng VietQR: MB, VCB, TCB, ACB, TPB, …
`AccountHolderName`                   | ✅      | Tên chủ tài khoản (hiển thị cho user)
`QrTemplate`                          | ⛔      | `compact` (default) / `compact2` / `qr_only` / `print`
`HmacSecret`                          | ✅      | Khoá HMAC-SHA256 chia sẻ với SEPay dashboard. Trống ⇒ fallback Bearer token (`WebhookSecret`).
`WebhookSecret`                       | ⛔      | Legacy bearer token (chỉ dùng khi `HmacSecret` trống).
`WebhookTimestampToleranceSeconds`    | ⛔      | 60-600s (default 300s) — chống replay
`PaymentSessionExpiresAfterMinutes`   | ⛔      | QR có hiệu lực bao lâu (default 30 min)

**Dev secrets** — không commit:
```bash
cd src/Services/Payment/UrbanX.Payment.API
dotnet user-secrets init
dotnet user-secrets set "SePay:BankAccount" "0123456789"
dotnet user-secrets set "SePay:BankCode" "MB"
dotnet user-secrets set "SePay:AccountHolderName" "URBANX SHOP"
dotnet user-secrets set "SePay:HmacSecret" "<32+ char>"
```

## QR URL spec

Static URL, không gọi API SEPay:

```
https://qr.sepay.vn/img?acc={BankAccount}&bank={BankCode}&amount={Amount}&des={OrderNumber}&template={QrTemplate}
```

`des={OrderNumber}` là nội dung chuyển khoản — bắt buộc giữ nguyên để webhook handler có thể match.

## HMAC verification spec

**Header**
- `X-SePay-Signature: sha256=<hex>`
- `X-SePay-Timestamp: <unix-seconds>`

**Signed payload**
```
{timestamp}.{rawBody}
```
(dấu chấm `.` ngăn cách, không có whitespace).

**Server check** ([SePayWebhookAuthFilter.cs](../../src/Services/Payment/UrbanX.Payment.API/Filters/SePayWebhookAuthFilter.cs))
1. Reject nếu `|now - timestamp| > WebhookTimestampToleranceSeconds`
2. Đọc raw body (đã `EnableBuffering` để endpoint sau đọc lại được)
3. Compute `HMACSHA256(secret, signedPayload)` → so sánh constant-time với header

## Test webhook bằng curl

```bash
TIMESTAMP=$(date +%s)
BODY='{"id":12345,"content":"CK UX-ORD-001 thanh toan","transferAmount":150000,"transferType":"in"}'
SECRET="<HmacSecret>"
SIG=$(printf '%s.%s' "$TIMESTAMP" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $NF}')

curl -X POST http://localhost:5000/api/v1/payments/webhook/sepay \
  -H "Content-Type: application/json" \
  -H "X-SePay-Signature: sha256=$SIG" \
  -H "X-SePay-Timestamp: $TIMESTAMP" \
  -d "$BODY"
```

Trả về `{ "success": true, "message": "..." }` nếu match được Payment + cập nhật trạng thái.

## Edge cases

Tình huống                          | Handler hành xử
---                                | ---
Webhook trùng (cùng `id`)          | `paymentEventRepository.ExistsByExternalTransactionIdAsync` dedup → return Success, không double-credit
Memo chứa Order# nhưng Payment EXPIRED | Ghi `PaymentEventTypes.WebhookReceivedAfterExpiry`, return Success. Cần refund thủ công.
Thiếu/sai chữ ký HMAC              | 401 Unauthorized
Timestamp lệch > tolerance         | 401 Unauthorized (chống replay)
Memo không match Order# nào        | Resolve trả null → return `{success:true, message:"no match"}` (không fail SEPay retry)
Underpayment                       | `MarkPartiallyPaid` → vẫn PENDING, đợi CK bù
Overpayment                        | `MarkCompletedViaBankTransfer` + ghi `WebhookOverpayment` event (refund chênh thủ công)
Payment hết hạn (`ExpiresAt < now`) | `PaymentExpirySweepJob` (Hangfire) chuyển status → EXPIRED + publish `PaymentExpiredV1`

## Gateway routing

Route YARP `/api/v1/payments/{**catch-all}` đã có sẵn. Webhook được khai báo public trong [`appsettings.json`](../../src/Gateway/UrbanX.Gateway/appsettings.json):

```json
"GatewayRbac": {
  "Public": [
    { "Method": "POST", "PathPrefix": "/api/v1/payments/webhook/sepay" }
  ]
}
```

⇒ SEPay (không có JWT) có thể POST vào mà không bị 401.

## Seed data

Migration `20260523121701_SeedSePayProvider` insert 1 row `payment_providers` với `type='SEPAY'`, `is_active=true`. Handler dùng `IPaymentProviderRepository.GetActiveByTypeAsync(ProviderType.Sepay)`.

## Non-goals

- IP allowlist SEPay (chỉ dùng HMAC + Gateway rate limit là đủ cho project học)
- Refund qua API SEPay (SEPay không hỗ trợ refund tự động — dùng `CompleteRefund` command với `providerRefundId` nhập tay)
- Multi-tenant bank account (1 merchant 1 account)
- Dynamic QR qua SEPay API (chỉ dùng VietQR URL tĩnh)
