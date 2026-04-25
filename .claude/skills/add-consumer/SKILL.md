# Skill: add-consumer

Thêm một MassTransit consumer vào service bất kỳ để xử lý integration event từ `Shared.Contract`.

---

## Inputs cần thiết

| Input | Ví dụ |
|---|---|
| Target service | `Inventory` |
| Integration event (full type) | `ProductIntegrationEvents.ProductCreatedV1` |
| Namespace của event trong Shared.Contract | `Shared.Contract.Messaging.Catalog` |
| Tên consumer class | `ProductCreatedInventoryConsumer` |

---

## Các bước thực hiện

### Bước 1 — Tạo consumer file

**Đường dẫn:** `src/Services/<Service>/UrbanX.<Service>.Application/Messaging/<ConsumerName>.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using <EventNamespace>;
using Shared.Messaging;

namespace UrbanX.<Service>.Application.Messaging;

public sealed class <ConsumerName>
    : IntegrationEventConsumerBase<
        <EventType>,
        <ConsumerName>>
{
    public <ConsumerName>(
        IMediator mediator,
        ILogger<<ConsumerName>> logger)
        : base(mediator, logger)
    {
    }

    protected override async Task HandleAsync(
        <EventType> @event,
        CancellationToken cancellationToken)
    {
        // TODO: implement handler logic
        // Default: base.HandleAsync publishes as MediatR notification
        await base.HandleAsync(@event, cancellationToken);
    }
}
```

**Lưu ý:**
- Dùng `sealed` trừ khi cần kế thừa
- Nếu chỉ cần dispatch sang MediatR (publish as notification), xóa override `HandleAsync` — base class đã làm điều đó mặc định
- Nếu cần custom logic (gọi command, ghi DB trực tiếp), override và implement trong `HandleAsync`

---

### Bước 2 — Đăng ký consumer trong Program.cs của API

**File:** `src/Services/<Service>/UrbanX.<Service>.API/Program.cs`

Tìm đoạn `AddMessaging(configureBus: bus =>` và thêm dòng:

```csharp
bus.AddConsumer<<ConsumerName>>();
```

**Nếu chưa có `AddMessaging`** (service chưa setup messaging), thêm toàn bộ block:

```csharp
using UrbanX.<Service>.Application.Messaging;

// Sau dòng AddApplication(...)
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(configureBus: bus =>
    {
        bus.AddConsumer<<ConsumerName>>();
    });
```

Và thêm using ở đầu file:
```csharp
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.<Service>.Application.Messaging;
```

---

### Bước 3 — Kiểm tra ProjectReference trong .csproj

File `UrbanX.<Service>.Application.csproj` phải có reference đến `Shared.Contract`:

```xml
<ProjectReference Include="..\..\..\..\..\Shared\Shared.Contract\Shared.Contract.csproj" />
<ProjectReference Include="..\..\..\..\..\Shared\Shared.Messaging\Shared.Messaging.csproj" />
```

Nếu chưa có, thêm vào `.csproj`. Không sửa `Directory.Packages.props` vì đây là ProjectReference, không phải NuGet package.

---

### Bước 4 — Tạo doc

**File:** `docs/<service>/<consumer-name>.md`

Nội dung tối thiểu:
- Event được consume
- Logic xử lý trong HandleAsync
- Service nào publish event đó

---

## Checklist hoàn thành

- [ ] Consumer file tạo đúng đường dẫn `*.Application/Messaging/`
- [ ] Class kế thừa `IntegrationEventConsumerBase<TEvent, TConsumer>`
- [ ] `bus.AddConsumer<>()` đã được đăng ký trong `Program.cs`
- [ ] ProjectReference đến `Shared.Contract` và `Shared.Messaging` có trong `.csproj`
- [ ] Build pass: `dotnet build`
- [ ] Doc được tạo

---

## Ví dụ thực tế — Search service

Consumer: `ProductCreatedConsumer` consume `ProductIntegrationEvents.ProductCreatedV1`

```csharp
// src/Services/Search/UrbanX.Search.Application/Messaging/ProductCreatedConsumer.cs
public class ProductCreatedConsumer
    : IntegrationEventConsumerBase<
        ProductIntegrationEvents.ProductCreatedV1,
        ProductCreatedConsumer>
{
    public ProductCreatedConsumer(
        IMediator mediator,
        ILogger<ProductCreatedConsumer> logger)
        : base(mediator, logger)
    {
    }

    protected override async Task HandleAsync(
        ProductIntegrationEvents.ProductCreatedV1 @event,
        CancellationToken cancellationToken)
    {
        // Custom logic — index product vào Elasticsearch
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}
```

DI trong `Program.cs`:
```csharp
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(configureBus: bus =>
    {
        bus.AddConsumer<ProductCreatedConsumer>();
        bus.AddConsumer<ProductCatalogUpdatedConsumer>();
        // ...
    });
```

---

## Base class reference

`IntegrationEventConsumerBase<TEvent, TConsumer>` từ `Shared.Messaging`:
- Implements `IConsumer<TEvent>` (MassTransit)
- Tự log EventId, EventName, CorrelationId
- Xử lý retry với transient exceptions (`TimeoutException`, `TaskCanceledException`)
- Default `HandleAsync`: publish event như MediatR notification (`INotification`)
- Override `IsTransient(Exception)` để custom retry logic nếu cần
