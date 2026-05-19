# Demo Payment Session Consumer

## Mục đích

Stub `CreatePaymentSessionConsumer` để **unblock saga `PlaceOrderNormal` / `PlaceOrderSales`** ở state `PaymentSessionCreating`. Saga publish `CreatePaymentSessionV1`, consumer tiêu thụ và publish ngay `PaymentSessionCreatedV1` với URL demo — không gọi provider thật, không ghi DB.

Khi tích hợp real provider (VNPay / SePay / Momo …), thay logic trong `HandleAsync` bằng command handler thật + Outbox publish.

## Vị trí code

| Loại | Đường dẫn |
|---|---|
| Consumer | [src/Services/Payment/UrbanX.Payment.Application/Messaging/CreatePaymentSession/CreatePaymentSessionConsumer.cs](../../src/Services/Payment/UrbanX.Payment.Application/Messaging/CreatePaymentSession/CreatePaymentSessionConsumer.cs) |
| Đăng ký bus | [src/Services/Payment/UrbanX.Payment.API/Program.cs](../../src/Services/Payment/UrbanX.Payment.API/Program.cs) — `bus.AddConsumer<CreatePaymentSessionConsumer>()` |

## Event flow

```
Order saga (PaymentSessionCreating)
    │  Publish CreatePaymentSessionV1
    ▼
Payment.CreatePaymentSessionConsumer
    │  Publish PaymentSessionCreatedV1 (demo URL)
    ▼
Order saga (PaymentPending)
```

| Hướng | Event | Source contract |
|---|---|---|
| Saga → Payment | `CreatePaymentSessionV1` | [Shared.Contract/Messaging/Payment/CreatePaymentSessionV1.cs](../../src/Shared/Shared.Contract/Messaging/Payment/CreatePaymentSessionV1.cs) |
| Payment → Saga | `PaymentSessionCreatedV1` | [Shared.Contract/Messaging/Payment/PaymentSessionCreatedV1.cs](../../src/Shared/Shared.Contract/Messaging/Payment/PaymentSessionCreatedV1.cs) |

## Demo response

```text
PaymentSessionId = Guid.NewGuid().ToString("N")
PaymentUrl       = https://demo.payment.local/checkout/{OrderId:N}
QrCodeUrl        = https://demo.payment.local/qr/{OrderId:N}.png
ExpiresAt        = UtcNow + 15 minutes
CorrelationId    = OrderId
CausationId      = incoming EventId
```

`PaymentSessionCompletedV1` (event báo đã thanh toán) **không** được consumer này publish — phải đến từ provider webhook thật. Trong môi trường demo cuối-end-to-end, có thể trigger thủ công qua endpoint SePay webhook (xem [sepay-webhook.md](sepay-webhook.md)).

## Config / env

Không có config riêng — consumer dùng default endpoint do MassTransit + RabbitMQ tự tạo (`CreatePaymentSession` queue).
