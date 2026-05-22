---
name: add-consumer
description: Thêm MassTransit consumer vào một service để xử lý integration event từ Shared.Contract. Tự động invoke khi user yêu cầu "thêm consumer", "subscribe event", "consume message/event", "lắng nghe event", hoặc khi cần một service phản ứng với event do service khác publish (ví dụ: "khi Catalog publish ProductCreated thì Inventory phải xử lý").
allowed-tools: Read, Grep, LS, Write, Edit, MultiEdit
---
# Skill: add-consumer

Thêm MassTransit consumer vào service để xử lý integration event từ `Shared.Contract`. **Reference pattern: Inventory service** ([ReserveInventoryRequestedConsumer.cs](src/Services/Inventory/UrbanX.Inventory.Infrastructure/Messaging/ReserveInventoryRequested/ReserveInventoryRequestedConsumer.cs)).

---

## Pattern cốt lõi

- Consumer **trực tiếp** implement `IConsumer<TEvent>` (MassTransit) — KHÔNG dùng `IntegrationEventConsumerBase`, KHÔNG Processor class trung gian, KHÔNG `CommandFailedException` indirection.
- Consumer chỉ làm 1 việc: map event → command → `_sender.Send(...)` qua MediatR.
- **Command dispatched từ consumer phải là `IMessagingCommand` / `IMessagingCommand<T>`** (KHÔNG dùng `ICommand`). Lý do: MT EF Outbox đã wrap consumer trong DbContext transaction + dedup qua `inbox_state`; nếu dùng `ICommand` → `TransactionPipelineBehavior` sẽ chạy lại gây "already in transaction" hoặc rollback semantics vỡ.
- **Vị trí**: `src/Services/<Service>/UrbanX.<Service>.Infrastructure/Messaging/<EventName>/` — KHÔNG đặt trong Application.
- Options/Validator của consumer (queue name, retry, prefetch, concurrent limit) đặt tại `Infrastructure/DependencyInjection/Options/`.
- Đăng ký consumer scope DI ở `Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` (`AddInfrastructure()`).
- Đăng ký bus ở `Program.cs`: `bus.AddConsumer<XConsumer>(typeof(XConsumerDefinition))`.

---

## Inputs cần thiết

| Input | Ví dụ |
|---|---|
| Target service | `Inventory` |
| Integration event (full type) | `ProductCreatedV1` |
| Event namespace | `Shared.Contract.Messaging.Catalog` |
| Tên consumer | `ProductCreatedInventoryConsumer` |
| Section name appsettings | `Inventory:Messaging:ProductCreated` |
| Command dispatch (đã có?) | `IndexProductCommand`, hoặc cần tạo (gọi skill `add-command`) |

---

## Các bước thực hiện

### Bước 0 — Đảm bảo Command target đã dùng `IMessagingCommand`

Command sẽ được consumer gọi (qua `_sender.Send`) phải implement `IMessagingCommand` (không `ICommand`):

```csharp
// Application/Usecases/V1/Command/<Feature>/<X>Command.cs
[AllowAnonymous]
public record <X>Command(Guid OrderId, ...) : IMessagingCommand;

// Handler
internal sealed class <X>CommandHandler(...) : IMessagingCommandHandler<<X>Command>
{
    public Task<Result> Handle(<X>Command cmd, CancellationToken ct)
    {
        // KHÔNG gọi SaveChanges — MT auto-commit sau khi Consume return
        // throw để rollback (MT retry / DLQ); return Result.Failure cho business decision
        return Task.FromResult(Result.Success());
    }
}
```

Nếu command đang dùng `ICommand` và bị gọi từ cả API + consumer → **tách 2 command** (1 cho API dùng `ICommand`, 1 cho consumer dùng `IMessagingCommand`); KHÔNG share một command duy nhất cho cả 2 path.

---

### Bước 1 — Tạo Consumer

**File:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/Messaging/<EventName>/<ConsumerName>.cs`

```csharp
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using <EventNamespace>;
using UrbanX.<Service>.Application.Usecases.V1.Command.<Feature>;

namespace UrbanX.<Service>.Infrastructure.Messaging;

public sealed class <ConsumerName> : IConsumer<<EventType>>
{
    private readonly ISender _sender;
    private readonly ILogger<<ConsumerName>> _logger;

    public <ConsumerName>(ISender sender, ILogger<<ConsumerName>> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<<EventType>> context)
    {
        var command = new <CommandName>(
            // map từ context.Message.* sang field của command
            OrderId: context.Message.OrderId,
            EventId: context.Message.EventId);

        await _sender.Send(command, context.CancellationToken);
    }
}
```

**Rules:**
- `sealed` mặc định.
- Namespace **bắt buộc** khớp project: `UrbanX.<Service>.Infrastructure.Messaging`.
- KHÔNG kế thừa `IntegrationEventConsumerBase`. KHÔNG tạo Processor / FailedException / TransientClassifier riêng — Command handler trả `Result.Failure(...)` (bị retry hoặc DLQ tùy `ConsumerDefinition`), throw cho lỗi transient.

---

### Bước 2 — Tạo ConsumerDefinition (cùng folder)

**File:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/Messaging/<EventName>/<ConsumerName>Definition.cs`

```csharp
using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.<Service>.Infrastructure.DependencyInjection.Options;

namespace UrbanX.<Service>.Infrastructure.Messaging;

public sealed class <ConsumerName>Definition : ConsumerDefinition<<ConsumerName>>
{
    private readonly <ConsumerName>Options _options;

    public <ConsumerName>Definition(IOptions<<ConsumerName>Options> options)
    {
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<<ConsumerName>> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.RetryLimit > 0)
        {
            endpointConfigurator.UseMessageRetry(r => r.Exponential(
                retryLimit: retry.RetryLimit,
                minInterval: TimeSpan.FromMilliseconds(retry.MinIntervalMs),
                maxInterval: TimeSpan.FromMilliseconds(retry.MaxIntervalMs),
                intervalDelta: TimeSpan.FromMilliseconds(retry.IntervalDeltaMs)));
        }

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            if (_options.PrefetchCount is { } prefetch && prefetch > 0)
                rabbit.PrefetchCount = prefetch;
            if (_options.ConcurrentMessageLimit is > 0)
                rabbit.ConcurrentMessageLimit = _options.ConcurrentMessageLimit;

            // Optional — bind tới fanout exchange (ví dụ compensation events):
            // rabbit.Bind("compensation.events", b => b.ExchangeType = "fanout");
        }
    }
}
```

**Pattern retry/prefetch:**
- `AddMessaging` (Shared.Messaging) **không** bật retry/prefetch toàn bus. Cấu hình per consumer ở Definition này.
- Set `RetryLimit = 0` (hoặc bỏ qua) → tắt broker retry cho consumer này.
- Set `MinIntervalMs > MaxIntervalMs` → fail validation (xem Step 3).

---

### Bước 3 — Tạo Options + Validator

**File:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/DependencyInjection/Options/<ConsumerName>Options.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace UrbanX.<Service>.Infrastructure.DependencyInjection.Options;

public sealed class <ConsumerName>Options
{
    public const string SectionName = "<Service>:Messaging:<EventName>";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    [Required]
    public <ConsumerName>RetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}

public sealed class <ConsumerName>RetryOptions
{
    [Range(0, 100)]
    public int RetryLimit { get; set; } = 3;

    [Range(0, 60_000)]
    public int MinIntervalMs { get; set; } = 200;

    [Range(0, 300_000)]
    public int MaxIntervalMs { get; set; } = 2_000;

    [Range(0, 60_000)]
    public int IntervalDeltaMs { get; set; } = 500;
}
```

**File:** `<ConsumerName>OptionsValidator.cs` (cùng folder, `internal sealed`):

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.<Service>.Infrastructure.DependencyInjection.Options;

internal sealed class <ConsumerName>OptionsValidator
    : IValidateOptions<<ConsumerName>Options>
{
    public ValidateOptionsResult Validate(string? name, <ConsumerName>Options options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        if (options.Retry is not null)
            Validator.TryValidateObject(options.Retry, new ValidationContext(options.Retry), results, validateAllProperties: true);

        if (options.Retry is { } retry && retry.MaxIntervalMs < retry.MinIntervalMs)
            results.Add(new ValidationResult("MaxIntervalMs must be >= MinIntervalMs.",
                [nameof(<ConsumerName>RetryOptions.MaxIntervalMs)]));

        if (options.QueueName is not null && options.QueueName.Length > 0 && string.IsNullOrWhiteSpace(options.QueueName))
            results.Add(new ValidationResult("QueueName cannot be whitespace-only.",
                [nameof(<ConsumerName>Options.QueueName)]));

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
            results.Add(new ValidationResult("PrefetchCount must be omitted or > 0.",
                [nameof(<ConsumerName>Options.PrefetchCount)]));

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
            results.Add(new ValidationResult("ConcurrentMessageLimit must be omitted or > 0.",
                [nameof(<ConsumerName>Options.ConcurrentMessageLimit)]));

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
```

---

### Bước 4 — Đăng ký trong `AddInfrastructure()`

**File:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

Thêm vào method `AddInfrastructure(IServiceCollection services)`:

```csharp
services.AddSingleton<IValidateOptions<<ConsumerName>Options>, <ConsumerName>OptionsValidator>();
services
    .AddOptions<<ConsumerName>Options>()
    .BindConfiguration(<ConsumerName>Options.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

> Consumer KHÔNG cần `AddScoped<XConsumer>()` — MassTransit tự register khi `bus.AddConsumer<XConsumer>(typeof(XDef))` trong Program.cs.

---

### Bước 5 — Đăng ký bus trong `Program.cs`

**File:** `src/Services/<Service>/UrbanX.<Service>.API/Program.cs`

Trong block `AddMessaging(builder.Configuration, configureBus: bus => { ... })`, thêm:

```csharp
bus.AddConsumer<<ConsumerName>>(typeof(<ConsumerName>Definition));
```

Đảm bảo `using UrbanX.<Service>.Infrastructure.Messaging;` có ở đầu file.

---

### Bước 6 — Bind config trong appsettings.json

**File:** `src/Services/<Service>/UrbanX.<Service>.API/appsettings.json`

```json
{
  "<Service>": {
    "Messaging": {
      "<EventName>": {
        "QueueName": "<service>.<event>",
        "Retry": {
          "RetryLimit": 3,
          "MinIntervalMs": 200,
          "MaxIntervalMs": 2000,
          "IntervalDeltaMs": 500
        },
        "PrefetchCount": 16,
        "ConcurrentMessageLimit": 8
      }
    }
  }
}
```

> Có thể omit `PrefetchCount`/`ConcurrentMessageLimit` để dùng transport default.

---

### Bước 7 — Kiểm tra reference

**`UrbanX.<Service>.Infrastructure.csproj`** đã có:

```xml
<PackageReference Include="MassTransit.RabbitMQ" />
<PackageReference Include="MediatR" />
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />

<ProjectReference Include="..\UrbanX.<Service>.Application\..." />
<ProjectReference Include="..\..\..\Shared\Shared.Contract\..." />
<ProjectReference Include="..\..\..\Shared\Shared.Messaging\..." />
```

Application.csproj KHÔNG cần MassTransit (consumer giờ ở Infrastructure).

---

### Bước 8 — Tạo doc

**File:** `docs/<service>/<consumer-kebab-case>.md`

Nội dung tối thiểu:
- Event được consume (từ service nào publish)
- Command nào được dispatch
- Section appsettings + giá trị mặc định
- Retry policy + lý do (transient vs permanent error)
- Queue / exchange binding nếu non-default

---

## Checklist hoàn thành

- [ ] Consumer file đặt `Infrastructure/Messaging/<EventName>/` với namespace `UrbanX.<Service>.Infrastructure.Messaging`
- [ ] Consumer implement `IConsumer<TEvent>` trực tiếp, dispatch qua `ISender`
- [ ] ConsumerDefinition đọc Options qua `IOptions<T>`, set retry/prefetch/concurrent limit từ config
- [ ] Options + Validator ở `Infrastructure/DependencyInjection/Options/`
- [ ] `AddInfrastructure()` register options + validator (Singleton)
- [ ] `Program.cs` gọi `bus.AddConsumer<X>(typeof(XDefinition))`
- [ ] `appsettings.json` có section `<Service>:Messaging:<EventName>`
- [ ] Infrastructure.csproj có MassTransit.RabbitMQ + MediatR + Shared.Messaging refs
- [ ] Build pass: `dotnet build`
- [ ] Doc: `docs/<service>/<consumer>.md`

---

## Anti-patterns (đừng làm)

- ❌ Đặt Consumer trong `Application/Messaging/` — đã chuyển sang Infrastructure
- ❌ Kế thừa `IntegrationEventConsumerBase` — pattern cũ, đã drop
- ❌ Tách Processor class (`XProcessor`) làm trung gian giữa Consumer và MediatR — gộp luôn vào Consumer
- ❌ Tạo `XCommandFailedException` + override `IsTransient` để classify lỗi — dùng `Result.Failure(Domain/Errors/...)` ở Command handler thay vì exception
- ❌ Hardcode queue name / retry interval trong code — luôn qua Options + `appsettings`
- ❌ `services.AddScoped<XConsumer>()` trong `AddInfrastructure()` — MassTransit tự quản lý lifetime
- ❌ Đặt Options trong `Application/` — luôn ở `Infrastructure/DependencyInjection/Options/`
- ❌ Command dispatched từ consumer dùng `ICommand` thay vì `IMessagingCommand` — sẽ bị `TransactionPipelineBehavior` wrap lại trong DbContext transaction (MT đã có sẵn) → "already in transaction" hoặc rollback vỡ
- ❌ Custom `ProcessedEvent` table cho dedup — MT InboxState (auto-enable qua `AddInboxStateEntity()` + `DuplicateDetectionWindow`) đã handle
- ❌ Gọi `SaveChangesAsync` / `_uow.ExecuteInTransactionAsync` trong handler của `IMessagingCommand` — MT auto-commit sau `Consume`

---

## Ví dụ thực tế

Xem [ReserveInventoryRequestedConsumer.cs](src/Services/Inventory/UrbanX.Inventory.Infrastructure/Messaging/ReserveInventoryRequested/ReserveInventoryRequestedConsumer.cs) + [ReserveInventoryRequestedConsumerDefinition.cs](src/Services/Inventory/UrbanX.Inventory.Infrastructure/Messaging/ReserveInventoryRequested/ReserveInventoryRequestedConsumerDefinition.cs) + [ReserveInventoryRequestedConsumerOptions.cs](src/Services/Inventory/UrbanX.Inventory.Infrastructure/DependencyInjection/Options/ReserveInventoryRequestedConsumerOptions.cs).
