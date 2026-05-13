# Architecture Rules — UrbanX

Clean Architecture per-service, .NET 10, CQRS via MediatR, Carter endpoints, EF Core + PostgreSQL.

---

## 1. Layer Structure (per service)

```
<Service>.Domain/
  Models/, ValueObjects/, Repositories/   ← Entity, VO, repository interfaces
  Errors/                                 ← Mã lỗi nghiệp vụ (Shared.Kernel Error): OrderErrors, *AllocationErrors, …
<Service>.Infrastructure/
  RefitApi/<Context>/                     ← Refit contract + DTO wire JSON (Inventory, Coupon, …)
  Services/                               ← HttpClient / adapter implement port từ Application
  DependencyInjection/                     ← AddInfrastructure, Options
<Service>.Persistence/                    ← DbContext, EF configs, Repository impl, Migrations, EfUnitOfWork
<Service>.Application/
  Clients/, Abstractions/                 ← Port interfaces (IInventoryClient, ISaleAllocationGate, …)
  Exceptions/                             ← Exception biên HTTP/client (handler catch → map Result)
  Usecases/V1/…                           ← Command, Query, Handler, Validator, Consumers
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

**Infrastructure**
- Adapters: HTTP (`HttpClient`), **Refit** (`Infrastructure/RefitApi/...`), Redis, email, gateway…
- **Implement** interface từ `Application/Clients` hoặc `Application/Abstractions` — không duplicate port chỉ để tránh reference Application (trừ legacy)
- Refit: interface + request/response DTO nằm **Infrastructure**; response có thể map sang DTO/aggregate đã định nghĩa ở Application hoặc Domain tùy luồng

**Persistence**
- `<Service>DbContext` kế thừa `OutboxDbContext` (hoặc `DbContext` nếu không dùng Outbox)
- `EfUnitOfWork` implement `IUnitOfWork` (từ `Shared.Kernel.Primitives`)
- EF Configurations trong `Configurations/`, Repositories trong `Repositories/`
- `AddPersistence()` đăng ký `IUnitOfWork` + tất cả repositories
- **Application KHÔNG được reference Persistence**

**Application**
- **`Application/Clients`**, **`Application/Abstractions`**: port (interface) mà handler inject — Infrastructure implement
- **`Application/Exceptions`**: exception dùng khi **HTTP/client** báo lỗi (timeout, 4xx/5xx mapping); handler `catch` rồi chuyển sang `Result.Failure(Domain/Errors/...)` — **không** đặt các type này trong Domain
- `AddApplication()`: MediatR + pipeline behaviors — **không** đăng ký DbContext, Outbox relay, hay implementation Infrastructure
- KHÔNG reference **Persistence**; **không** reference **Infrastructure** (pattern khuyến nghị)

**API**
- Carter modules trong `Apis/` — không chứa business logic, không inject repository
- `Program.cs` là nơi duy nhất wire DI
- KHÔNG dùng `RequireAuthorization()` trên endpoints

---

## 2. Program.cs — Registration Order

```csharp
builder.AddServiceDefaults();
builder.AddSharedCache("redis");           // bắt buộc mọi service

builder.AddNpgsqlDbContext<XDbContext>("xdb");
builder.Services.AddOutbox<XDbContext>(..., config);  // nếu dùng Outbox

builder.Services
    .AddConfigMessaging(config)
    .AddMessaging(configureBus: bus =>    // khai báo consumers ở đây
    {
        bus.AddConsumer<SomeConsumer>();
    });

builder.Services.AddHealthChecks().AddDbContextCheck<XDbContext>(...);
builder.Services.AddProblemDetails();

builder.Services.AddInfrastructure(config);  // nếu có Infrastructure
builder.Services.AddPersistence();           // IUnitOfWork + repositories
builder.Services.AddApplication(config);     // MediatR + behaviors

builder.Services.AddApiVersioning(...).AddApiExplorer(...);
builder.Services.AddCarter();
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

### Command template

```csharp
// <Name>Command.cs
[RequirePermission(Permissions.X.Write)]   // hoặc [AllowAnonymous]
public record <Name>Command(...) : ICommand<Guid>;

public sealed class <Name>CommandValidator : AbstractValidator<<Name>Command>
{
    public <Name>CommandValidator()
    {
        RuleFor(x => x.Field).NotEmpty().MaximumLength(200);
    }
}
```

```csharp
// <Name>CommandHandler.cs
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
- **Legacy:** service chưa gom (ví dụ Catalog) có thể còn `Application/Usecases/V1/Errors/*Errors.cs` — khi sửa lớn nên chuyển dần sang **`Domain/Errors/`** cho thống nhất

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
public sealed class <Service>DbContext(DbContextOptions<<Service>DbContext> options)
    : OutboxDbContext(options)   // hoặc DbContext nếu không dùng Outbox
{
    public DbSet<Entity> Entities => Set<Entity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
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

**Publish (Outbox — at-least-once):**
```csharp
// Handler inject IOutboxWriter, KHÔNG inject IEventPublisher khi cần guarantee
await _outboxWriter.WriteAsync(new <Event>V1(...), ct);
```

**Consumer:**
```csharp
// Application/Messaging/<Event>Consumer.cs
public sealed class <Event>Consumer(ISender sender)
    : IntegrationEventConsumerBase<<Event>V1, <Event>Consumer>
{
    public override async Task Consume(ConsumeContext<<Event>V1> context) { ... }
}
```
Đăng ký trong `Program.cs`: `bus.AddConsumer<<Event>Consumer>()` trong `AddMessaging(configureBus: ...)`.

---

## 9. csproj Reference Rules

**Application.csproj** — references tối thiểu:
```xml
<ProjectReference Include="Shared.Application" />
<ProjectReference Include="Shared.Messaging" />
<ProjectReference Include="<Service>.Domain" />
<!-- Thêm nếu cần: Shared.Contract, Shared.Outbox, Shared.Cache -->
<!-- KHÔNG reference <Service>.Persistence -->
<!-- KHÔNG reference <Service>.Infrastructure (pattern khuyến nghị — ports trong Application) -->
```

**Infrastructure.csproj** (khi có):
```xml
<ProjectReference Include="<Service>.Application" />
<ProjectReference Include="<Service>.Domain" />
<!-- Shared.* theo adapter: Contract, Outbox, Cache, … -->
```

**API.csproj** — references tối thiểu:
```xml
<ProjectReference Include="ServiceDefaults" />
<ProjectReference Include="Shared.Cache" />
<ProjectReference Include="Shared.Messaging" />
<ProjectReference Include="<Service>.Application" />
<ProjectReference Include="<Service>.Persistence" />
<!-- Thêm nếu cần: Shared.Outbox, <Service>.Infrastructure, <Service>.Domain -->
```

**NuGet:** thêm package phiên bản centralized trong `Directory.Packages.props` — không thêm `<Version>` trùng trong `.csproj` (tránh drift phiên bản).
