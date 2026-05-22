# Architecture Rules — UrbanX

Clean Architecture per-service, .NET 10, CQRS via MediatR, Carter endpoints, EF Core + PostgreSQL.

---

## 1. Layer Structure (per service)

```
<Service>.Domain/
  Models/, ValueObjects/, Repositories/   ← Entity, VO, repository interfaces
  Errors/                                 ← Mã lỗi nghiệp vụ (Shared.Kernel Error): OrderErrors, *AllocationErrors, …
<Service>.Application/                    ← CHỈ CQRS + abstractions, không có implementation
  Clients/, Abstractions/                 ← Port interfaces (IInventoryClient, ISaleAllocationGate, …)
  Exceptions/                             ← Exception biên HTTP/client (handler catch → map Result)
  Usecases/V1/…                           ← Command, Query, Handler, Validator
<Service>.Infrastructure/                 ← MỌI implementation outbound (Inventory là mẫu)
  Messaging/<Event>/                      ← MassTransit IConsumer<T> + ConsumerDefinition
  Jobs/                                   ← Recurring jobs (Hangfire, …)
  RefitApi/<Context>/                     ← Refit contract + DTO wire JSON
  Services/                               ← HttpClient / adapter implement port từ Application
  DependencyInjection/Extensions/         ← AddInfrastructure() entry point
  DependencyInjection/Options/            ← Tất cả IOptions + IValidateOptions classes
<Service>.Persistence/                    ← DbContext, EF configs, Repository impl, Migrations, EfUnitOfWork
<Service>.API/                            ← Carter modules, Program.cs, appsettings
```

**Dependency order (khuyến nghị — tránh Application → Infrastructure):**
```
Domain ← Persistence
Domain ← Infrastructure

Application → Domain only
  (KHÔNG reference Persistence; KHÔNG reference Infrastructure)

Infrastructure → Application + Domain
  (implement port trong Application; dùng type Domain/Errors khi cần)

API → Application + Persistence + Infrastructure (+ Domain nếu layer API cần type Domain, ví dụ PriceMismatchError)
```

**Lưu ý:** Service cũ có thể cho Application reference Infrastructure (legacy). **Ưu tiên:** port outbound đặt trong **Application** (`Clients` / `Abstractions`), Infrastructure chỉ implement + đăng ký DI trong `Program.cs`.

### Constraints per layer

**Domain**
- Business logic thuần — không EF Core, MediatR, host DI
- **`Domain/Errors/`**: toàn bộ **mã lỗi nghiệp vụ** (`Error`, `readonly Error`, `record` kế `Error`) — handler dùng `Result.Failure(...)` với các hằng/method từ đây
- Entity kế thừa `BaseEntity<TKey>` (Shared.Kernel); repository interfaces trong `Repositories/`

**Infrastructure** (ref: Inventory)
- Adapters: HTTP (`HttpClient`), **Refit** (`Infrastructure/RefitApi/...`), Redis, email, gateway…
- **Implement** interface từ `Application/Clients` hoặc `Application/Abstractions` — không duplicate port chỉ để tránh reference Application (trừ legacy)
- Refit: interface + request/response DTO nằm **Infrastructure**; response có thể map sang DTO/aggregate đã định nghĩa ở Application hoặc Domain tùy luồng
- **MassTransit consumer** (`IConsumer<T>` + `ConsumerDefinition`), **recurring job** (Hangfire) đặt tại đây — KHÔNG đặt trong Application
- **Tất cả options đọc từ `appsettings`** (consumer/job/HTTP client) đặt tại `Infrastructure/DependencyInjection/Options/` với namespace `UrbanX.<Service>.Infrastructure.DependencyInjection.Options`; mỗi options class có `IValidateOptions<T>` validator đăng ký `Singleton`
- `AddInfrastructure()` ở `Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` register: options + validators (qua `AddOptions<T>().BindConfiguration(T.SectionName).ValidateDataAnnotations().ValidateOnStart()`), scoped processor/job, HTTP clients

**Persistence**
- `<Service>DbContext` kế thừa `DbContext` và register MT outbox entities (`AddInboxStateEntity`/`AddOutboxMessageEntity`/`AddOutboxStateEntity`) trong `OnModelCreating`
- `EfUnitOfWork` implement `IUnitOfWork` (từ `Shared.Kernel.Primitives`)
- EF Configurations trong `Configurations/`, Repositories trong `Repositories/`
- `AddPersistence()` đăng ký `IUnitOfWork` + tất cả repositories
- **Application KHÔNG được reference Persistence**

**Application** (ref: Inventory)
- **`Application/Clients`**, **`Application/Abstractions`**: port (interface) mà handler inject — Infrastructure implement
- **`Application/Exceptions`**: exception dùng khi **HTTP/client** báo lỗi (timeout, 4xx/5xx mapping); handler `catch` rồi chuyển sang `Result.Failure(Domain/Errors/...)` — **không** đặt các type này trong Domain
- `AddApplication()` chỉ register **MediatR + FluentValidation** — đúng 1 dòng: `services.AddMediatorWithPielineDefault(AssemblyReference.Assembly)`. KHÔNG register: DbContext, repository impl, HTTP client, consumer, processor, job, options.
- KHÔNG reference **Persistence**; **không** reference **Infrastructure** (pattern khuyến nghị)
- KHÔNG đặt MassTransit consumer/job/options trong Application (chuyển sang Infrastructure)

**API**
- Carter modules trong `Apis/` — không chứa business logic, không inject repository
- `Program.cs` là nơi duy nhất wire DI
- KHÔNG dùng `RequireAuthorization()` trên endpoints

---

## 2. Program.cs — Registration Order (ref Inventory)

```csharp
builder.AddServiceDefaults();
builder.AddSharedCache("redis");           // bắt buộc mọi service
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<XDbContext>("xdb", configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());

// Infrastructure trước — options/consumers/jobs/clients phải register trước khi MassTransit resolve ConsumerDefinition.
builder.Services.AddInfrastructure();

// Application — MediatR + FluentValidation (1 dòng)
builder.Services.AddApplication();

// MassTransit + EF Outbox: khai báo consumer ở đây (Definition đã được DI từ AddInfrastructure)
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddEntityFrameworkOutbox<XDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
        });

        bus.AddConsumer<SomeConsumer>(typeof(SomeConsumerDefinition));
    });

builder.Services.AddHealthChecks().AddDbContextCheck<XDbContext>(...);
builder.Services.AddProblemDetails();
builder.Services.AddPersistence();           // IUnitOfWork + repositories

builder.Services.AddApiVersioning(...).AddApiExplorer(...);
builder.Services.AddCarter();
// Optional: Hangfire (Inventory dùng cho TTL job)
```

```csharp
app.UseExceptionHandler();
app.UseUserContext();       // TRƯỚC MapCarter
// auto-migrate nếu cần
app.MapCarter();
```

---

## 3. CQRS — File & Folder Structure

```
Application/Usecases/V1/
  Command/<Name>/
    <Name>Command.cs          ← record Command + Validator (cùng file)
    <Name>CommandHandler.cs ← Handler
  Query/<Name>/
    <Name>Query.cs            ← record Query + Validator (cùng file)
    <Name>QueryHandler.cs     ← Handler
Application/Clients/         ← Port interfaces (HTTP clients gọi service khác)
Application/Abstractions/    ← Port khác (ví dụ allocation gate)
Application/Exceptions/      ← Exception biên (HTTP/client), không thay cho Domain/Errors

Domain/Errors/
  <Service>Errors.cs         ← Mã lỗi nghiệp vụ (Error) — nguồn chân lý cho Result.Failure
```

### Command markers — chọn đúng theo nguồn dispatch

| Marker | Inherits | Dispatched từ | Pipeline áp dụng |
|---|---|---|---|
| `ICommand` / `ICommand<T>` | `ICommandBase`, `IRequest<Result(<T>)>` | API endpoint (Carter) | Logging, Authorization, Validation, Idempotency*, DistributedLock*, **Transaction** |
| `IMessagingCommand` / `IMessagingCommand<T>` | `IRequest<Result(<T>)>` | MassTransit consumer | Logging, Authorization, Validation, DistributedLock* — **SKIP Transaction, SKIP Idempotency** |

*Idempotency chỉ kích hoạt nếu command implement `IIdempotentCommand`; DistributedLock chỉ kích hoạt nếu có `[DistributedLock]` attribute.

**Lý do skip Transaction cho IMessagingCommand:**
MT EF Outbox đã wrap consumer trong DbContext transaction (`EntityFrameworkBusOutboxConsumeFilter<TDbContext>` mở `BeginTransactionAsync` → run consumer → `SaveChangesAsync` + `CommitAsync` + insert `inbox_state`). Nếu `TransactionPipelineBehavior` chạy lại bên trong:
- Npgsql lỗi *"connection is already in a transaction"*, hoặc
- Tạo savepoint nested; handler trả `Result.Failure` → behavior catch + swallow `HandlerFailureAbortException` → consumer return normally → MT commit outer transaction kèm `inbox_state` ⇒ **silent "marked done nhưng chưa làm gì"**.

**Lý do skip Idempotency cho IMessagingCommand:**
MT InboxState (`inbox_state` table + `DuplicateDetectionWindow = 10 min` ở `AddEntityFrameworkOutbox(o => ...)`) đã dedup theo MessageId. Không cần Redis-based idempotency của `IdempotencyPipelineBehavior`.

**Mechanism:** Behavior `where`-clause tự nhiên skip:
- `TransactionPipelineBehavior` — `where TRequest : ICommandBase` (IMessagingCommand không kế thừa)
- `IdempotencyPipelineBehavior` — `where TRequest : IIdempotentCommand`

**Behavioral note cho IMessagingCommand handler:**
- **Không gọi `SaveChanges` thủ công** — MT auto-commit sau khi `Consume` return.
- **Không có auto-rollback** khi trả `Result.Failure`. Muốn rollback (transient/permanent error) → `throw`. MT retry policy (ở `ConsumerDefinition`) hoặc DLQ sẽ xử lý.
- Inject repository, modify entities, đừng wrap transaction nữa.

### Command template

```csharp
// <Name>Command.cs (API-dispatched)
[RequirePermission(Permissions.X.Write)]   // hoặc [AllowAnonymous]
public record <Name>Command(...) : ICommand<Guid>;

// <Name>Command.cs (consumer-dispatched — IMessagingCommand)
[AllowAnonymous]
public record <Name>Command(...) : IMessagingCommand;

public sealed class <Name>CommandValidator : AbstractValidator<<Name>Command>
{
    public <Name>CommandValidator()
    {
        RuleFor(x => x.Field).NotEmpty().MaximumLength(200);
    }
}
```

```csharp
// <Name>CommandHandler.cs (API-dispatched)
internal sealed class <Name>CommandHandler(
    I<Entity>Repository repo,
    IUserContext userContext)   // chỉ khi cần ownership check
    : ICommandHandler<<Name>Command, Guid>
{
    public async Task<Result<Guid>> Handle(<Name>Command cmd, CancellationToken ct)
    {
        // business rules → Result.Failure(<Service>Errors.X) với <Service>Errors trong Domain/Errors
        // mutate entity
        // await repo.AddAsync(entity, ct);
        return Result.Success(entity.Id);
    }
}
```

> **`TransactionPipelineBehavior` tự wrap trong transaction** — handler không gọi `SaveChanges`, không `BeginTransaction`.

### Query template

```csharp
[RequirePermission(Permissions.X.Read, MinScope = PermissionScope.Own)]
public record <Name>Query(Guid Id) : IQuery<SomeDto>;

public sealed class <Name>QueryValidator : AbstractValidator<<Name>Query>
{
    public <Name>QueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
```

---

## 4. Error codes & boundary exceptions

### 4.1 Mã lỗi nghiệp vụ (`Error`) — **Domain**

Đặt trong **`Domain/Errors/`** (ví dụ `OrderErrors.cs`, `OrderSaleAllocationErrors.cs`). Dùng `Shared.Kernel.Primitives.Error`.

```csharp
// Domain/Errors/<Service>Errors.cs
namespace UrbanX.<Service>.Domain.Errors;

public static class <Service>Errors
{
    public static Error NotFound(Guid id) =>
        new("<Entity>.NotFound", $"<Entity> {id} was not found");

    public static readonly Error AlreadyExists =
        new("<Entity>.AlreadyExists", "<Entity> already exists");
}
```

**Rules:**
- Code message: ưu tiên `"<Entity>.<PascalCase>"` hoặc mã ổn định đã thống nhất với API (`ORDER_NOT_FOUND`, …) — nhất quán trong service
- Luật nghiệp vụ → `Result.Failure(<Service>Errors.…)` — **không** `throw` cho lỗi nghiệp vụ đã quy ước trả `Result`
- Không hardcode chuỗi lỗi rải rác — luôn qua static trong `Domain/Errors`
- **KHÔNG chấp nhận** `Application/Usecases/V1/Errors/` cho service mới — chỉ dùng `Domain/Errors/`
- Service cũ còn errors trong Application: chuyển về `Domain/Errors/` khi sửa file đó (không tạo task migrate riêng)

### 4.2 Exception biên HTTP / client — **Application**

Đặt trong **`Application/Exceptions/`** (ví dụ `OutOfStockException`, `CouponUnavailableException`). Infrastructure (adapter) **có thể** `throw` các type này; handler **catch** rồi map sang `Result.Failure(Domain/Errors/...)`.

- **Không** đưa các exception mang semantic HTTP/Refit vào Domain — Domain giữ `Error` và luật aggregate.

### 4.3 API layer

- Nếu endpoint cần `is` / pattern trên subtype của `Error` (ví dụ `PriceMismatchError`), **API** có thể `ProjectReference` **Domain** — chỉ khi thật sự cần; còn không thì map theo `Error.Code` string.

---

## 5. Carter Endpoint Template

```csharp
// API/Apis/<Entity>Apis.cs
public class <Entity>Apis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/<service>/<entities>";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("<Entity>").MapGroup(BaseURL).HasApiVersion(1);

        v1.MapPost("/", Create);
        v1.MapGet("/{id:guid}", GetById);
    }

    private static async Task<IResult> Create(
        Create<Entity>Command cmd, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"{BaseURL}/{result.Value}", result.Value)
            : To<Service>Result(result);
    }
}
```

**Rules:**
- KHÔNG `RequireAuthorization()` — authorization qua attribute trên Command/Query
- KHÔNG inject DbContext hay repository — chỉ inject `ISender`
- `ApiEndpoint.To<Service>Result(result)` map `Error.Code` → HTTP status:
  - `*.NotFound` → 404 · `FORBIDDEN` → 403 · `AUTH_REQUIRED` → 401 · conflict → 409 · default → 400

---

## 6. EF Core Patterns

### DbContext

```csharp
using MassTransit;

public sealed class <Service>DbContext(DbContextOptions<<Service>DbContext> options)
    : DbContext(options)
{
    public DbSet<Entity> Entities => Set<Entity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
```

### EfUnitOfWork

```csharp
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly <Service>DbContext _dbContext;
    public EfUnitOfWork(<Service>DbContext dbContext) => _dbContext = dbContext;

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                await operation();
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch { await transaction.RollbackAsync(ct); throw; }
        });
    }
}
```

### Entity Configuration

```csharp
// Persistence/Configurations/<Entity>Configuration.cs
internal sealed class <Entity>Configuration : IEntityTypeConfiguration<<Entity>>
{
    public void Configure(EntityTypeBuilder<<Entity>> builder)
    {
        builder.ToTable(TableNames.<Entities>);  // Constants/TableNames.cs
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();  // app assigns GUID
    }
}
```

### AddPersistence

```csharp
public static IServiceCollection AddPersistence(this IServiceCollection services)
{
    services.AddScoped<IUnitOfWork, EfUnitOfWork>();
    services.AddScoped<I<Entity>Repository, <Entity>Repository>();
    return services;
}
```

---

## 7. Authorization Pattern

**Attribute trên Command/Query — không phải endpoint:**

```csharp
[RequirePermission(Permissions.Products.Write)]
[RequirePermission(Permissions.Products.Read, MinScope = PermissionScope.Own)]
[RequireRole(Roles.Admin)]
[AllowAnonymous]
```

**Rules:**
- KHÔNG hardcode permission string — dùng `Permissions.<Resource>.<Action>` constants
- KHÔNG thêm permission mới ở handler/command — thêm vào `Permissions.cs` trong `Shared.Application/Authorization/`
- Handler inject `IUserContext` chỉ khi cần `UserId` cho ownership check hoặc audit

---

## 8. Integration Events

**Định nghĩa contract** trong `Shared.Contract/Messaging/<Service>/`:
```csharp
public record <Event>V1(...) : IntegrationEventBase;
```

**Publish (MT EF Outbox — at-least-once):**
```csharp
// Handler inject IEventPublisher (Shared.Application). MT bus outbox tự intercept
// publish trong scope EF transaction → stage vào outbox_message khi SaveChanges commit.
await _eventPublisher.PublishAsync(new <Event>V1(...), ct);
```

Wiring trong `Program.cs` (xem section 2):
```csharp
bus.AddEntityFrameworkOutbox<<Service>DbContext>(o =>
{
    o.UsePostgres();
    o.UseBusOutbox();
    o.QueryDelay              = TimeSpan.FromSeconds(1);
    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
});
```

**Consumer (simple pattern — ref Inventory):**
```csharp
// Infrastructure/Messaging/<Event>/<Event>Consumer.cs
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace UrbanX.<Service>.Infrastructure.Messaging;

public sealed class <Event>Consumer : IConsumer<<Event>V1>
{
    private readonly ISender _sender;
    private readonly ILogger<<Event>Consumer> _logger;

    public <Event>Consumer(ISender sender, ILogger<<Event>Consumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<<Event>V1> context)
    {
        var command = new <SomeCommand>(
            OrderId: context.Message.OrderId,
            EventId: context.Message.EventId);

        await _sender.Send(command, context.CancellationToken);
    }
}
```

**Consumer rules:**
- Implement trực tiếp `IConsumer<TEvent>` (MassTransit) — KHÔNG dùng `IntegrationEventConsumerBase`, KHÔNG Processor class trung gian, KHÔNG `CommandFailedException` indirection.
- Inject `ISender` (MediatR) + `ILogger<T>`. Mapping event → command → `_sender.Send(...)`. Trả `Result.Failure(...)` cho lỗi nghiệp vụ (handler tự lo); throw cho lỗi transient (broker retry policy ở `ConsumerDefinition` xử lý).
- File namespace khớp project: `UrbanX.<Service>.Infrastructure.Messaging`.
- **Command dispatched từ consumer phải dùng `IMessagingCommand` / `IMessagingCommand<T>`** (KHÔNG `ICommand`) — xem mục `Command Markers` bên dưới.

**ConsumerDefinition** (đặt cùng namespace + folder với Consumer):
```csharp
public sealed class <Event>ConsumerDefinition : ConsumerDefinition<<Event>Consumer>
{
    private readonly <Event>ConsumerOptions _options;

    public <Event>ConsumerDefinition(IOptions<<Event>ConsumerOptions> options)
    {
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<<Event>Consumer> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.RetryLimit > 0)
            endpointConfigurator.UseMessageRetry(r => r.Exponential(
                retry.RetryLimit,
                TimeSpan.FromMilliseconds(retry.MinIntervalMs),
                TimeSpan.FromMilliseconds(retry.MaxIntervalMs),
                TimeSpan.FromMilliseconds(retry.IntervalDeltaMs)));

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            if (_options.PrefetchCount is > 0) rabbit.PrefetchCount = _options.PrefetchCount.Value;
            if (_options.ConcurrentMessageLimit is > 0) rabbit.ConcurrentMessageLimit = _options.ConcurrentMessageLimit;
            // Optional: rabbit.Bind("compensation.events", b => b.ExchangeType = "fanout");
        }
    }
}
```

**Options + Validator** đặt `Infrastructure/DependencyInjection/Options/`:
```csharp
namespace UrbanX.<Service>.Infrastructure.DependencyInjection.Options;

public sealed class <Event>ConsumerOptions
{
    public const string SectionName = "<Service>:Messaging:<Event>";
    [MaxLength(255)] public string? QueueName { get; set; }
    [Required] public <Event>RetryOptions Retry { get; set; } = new();
    public ushort? PrefetchCount { get; set; }
    [Range(1, 1024)] public int? ConcurrentMessageLimit { get; set; }
}
```

**Đăng ký** trong `AddInfrastructure()`:
```csharp
services.AddSingleton<IValidateOptions<<Event>ConsumerOptions>, <Event>ConsumerOptionsValidator>();
services.AddOptions<<Event>ConsumerOptions>()
    .BindConfiguration(<Event>ConsumerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Trong `Program.cs`** (`AddMessaging(configureBus: ...)`):
```csharp
bus.AddConsumer<<Event>Consumer>(typeof(<Event>ConsumerDefinition));
```

---

## 9. csproj Reference Rules

**Application.csproj** — references & packages tối thiểu (ref Inventory):
```xml
<ItemGroup>
  <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="Shared.Application" />
  <ProjectReference Include="Shared.Contract" />        <!-- chỉ khi handler dùng integration event DTO -->
  <ProjectReference Include="<Service>.Domain" />
</ItemGroup>
<!-- KHÔNG reference Shared.Messaging (consumer ở Infrastructure) -->
<!-- KHÔNG reference <Service>.Persistence -->
<!-- KHÔNG reference <Service>.Infrastructure -->
<!-- KHÔNG include MassTransit.* package -->
```

**Infrastructure.csproj** (ref Inventory):
```xml
<ItemGroup>
  <PackageReference Include="MassTransit.RabbitMQ" />
  <PackageReference Include="MediatR" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="<Service>.Application" />
  <ProjectReference Include="<Service>.Domain" />
  <ProjectReference Include="Shared.Kernel" />
  <ProjectReference Include="Shared.Contract" />
  <ProjectReference Include="Shared.Messaging" />
  <!-- HTTP clients: + Microsoft.Extensions.Http.Resilience, Shared.Cache -->
</ItemGroup>
```

**API.csproj** — references tối thiểu (ref Inventory):
```xml
<ProjectReference Include="ServiceDefaults" />
<ProjectReference Include="Shared.Cache" />
<ProjectReference Include="<Service>.Domain" />
<ProjectReference Include="<Service>.Application" />
<ProjectReference Include="<Service>.Infrastructure" />
<ProjectReference Include="<Service>.Persistence" />
```

**NuGet:** thêm package phiên bản centralized trong `Directory.Packages.props` — không thêm `<Version>` trùng trong `.csproj` (tránh drift phiên bản).

---

## 10. Configuration — No Magic Values

Mọi giá trị cấu hình (thresholds, timeouts, limits, URLs, API keys, feature flags, v.v.) phải đặt trong `appsettings.json` và bind qua typed Options class. **Không hardcode magic value trong code.**

### Options pattern

```csharp
// Infrastructure/DependencyInjection/Options/StripeOptions.cs
// (Mọi options đọc từ appsettings — consumer/job/HTTP client — đều đặt tại đây)
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
}
```

```json
// appsettings.json
{
  "Stripe": {
    "SecretKey": "",
    "WebhookSecret": ""
  }
}
```

```csharp
// Program.cs (hoặc AddInfrastructure / AddApplication)
builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration(StripeOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Rules:**
- `SectionName` là `const` trên Options class — không dùng string literal khi `BindConfiguration`
- Inject `IOptions<T>` (singleton) hoặc `IOptionsSnapshot<T>` (per-request) — không inject raw value
- `.ValidateOnStart()` bắt buộc — fail fast lúc startup thay vì runtime
- Secrets (API key, password) đặt trong `appsettings.Development.json` (local) hoặc environment variable — không commit vào source control
- Options class đặt trong layer thấp nhất cần dùng: `Infrastructure/DependencyInjection/Options/` nếu chỉ Infrastructure dùng; `Application/Options/` nếu handler cần đọc
