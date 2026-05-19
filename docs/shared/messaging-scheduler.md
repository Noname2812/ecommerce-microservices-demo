# MassTransit Delayed Message Scheduler

## Thay đổi

`UsePublishMessageScheduler()` đã được thay bằng `UseDelayedMessageScheduler()` trong `Shared.Messaging/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`.

## Lý do

`UsePublishMessageScheduler` dùng in-memory scheduling — không bền vững, mất schedule khi restart. `UseDelayedMessageScheduler` dùng RabbitMQ delayed-message plugin, đảm bảo timeout saga (InventoryTimeout, CouponTimeout, PaymentExpiry, v.v.) vẫn fire đúng hạn ngay cả khi service restart.

## Yêu cầu AppHost

RabbitMQ phải dùng image `masstransit/rabbitmq` (đã có sẵn delayed-message plugin):

```csharp
// src/AppHost/UrbanX.AppHost/AppHost.cs
var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithImage("masstransit/rabbitmq");
```

Image mặc định (`rabbitmq:management`) không có plugin này — saga `Schedule(...)` sẽ throw lúc publish.

## Ảnh hưởng

Mọi saga dùng `Schedule<TState, TTimeout>` (PlaceOrderNormalSagaStateMachine, PlaceSalesOrderSagaStateMachine) đều phụ thuộc vào delayed-message plugin. Không cần thay đổi code saga — chỉ cần image đúng.
