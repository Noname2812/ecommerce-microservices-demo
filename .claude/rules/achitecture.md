# Architecture Rules — UrbanX

Clean Architecture per-service, .NET 10, CQRS via MediatR, Carter endpoints, EF Core + PostgreSQL.

---

## 1. Layer Structure (per service)

```
<Service>.Domain/         ← Entity, Value Object, Repository interface, Domain Exception
<Service>.Infrastructure/ ← External clients, email, third-party adapters
<Service>.Persistence/    ← DbContext, EF configs, Repository impl, Migrations, EfUnitOfWork
<Service>.Application/    ← Command, Query, Handler, Validator, Error codes, Consumers
<Service>.API/            ← Carter modules, Program.cs, appsettings
```

**Dependency order:**
```
Domain ← Infrastructure
Domain ← Persistence
Domain + Infrastructure + Persistence ← Application
Application ← API
```

### Constraints per layer

**Domain**
- Business logic thuần — không có EF Core, MediatR, DI framework
- Entity kế thừa `BaseEntity<TKey>` (Shared.Kernel)
- Repository interfaces định nghĩa ở đây

**Infrastructure**
- Implementation của external concerns: HTTP clients, email, audit, payment gateway
- Interface của các service này định nghĩa ngay trong project này (không phải Domain)
- Application phải reference Infrastructure khi handlers inject interface từ đây (ví dụ: `IEmailSender`, `IIdentityAuditWriter`)

**Persistence**
- `<Service>DbContext` kế thừa `OutboxDbContext` (hoặc `DbContext` nếu không dùng Outbox)
- `EfUnitOfWork` implement `IUnitOfWork` (từ `Shared.Kernel.Primitives`)
- EF Configurations trong `Configurations/`, Repositories trong `Repositories/`
- `AddPersistence()` đăng ký `IUnitOfWork` + tất cả repositories
- **Application KHÔNG được reference Persistence**

**Application**
- `AddApplication()` chỉ gọi `AddMediatorWithPielineDefault(AssemblyReference.Assembly)` — KHÔNG đăng ký Persistence hay Infrastructure
- KHÔNG reference Persistence project

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
    <Name>CommandHandler.cs   ← Handler
  Query/<Name>/
    <Name>Query.cs            ← record Query + Validator (cùng file)
    <Name>QueryHandler.cs     ← Handler
  Errors/
    <Service>Errors.cs        ← static error codes
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
        // business rules → Result.Failure(<Service>Errors.X) nếu fail
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

## 4. Error Codes

```csharp
// Application/Usecases/V1/Errors/<Service>Errors.cs
public static class <Service>Errors
{
    public static Error NotFound(Guid id) =>
        new("<Entity>.NotFound", $"<Entity> {id} was not found");

    public static readonly Error AlreadyExists =
        new("<Entity>.AlreadyExists", "<Entity> already exists");
}
```

**Rules:**
- Error code format: `"<Entity>.<PascalCase>"`
- Handler trả `Result.Failure(...)` — KHÔNG throw exception cho business errors
- KHÔNG hardcode string error — luôn dùng static readonly hoặc static method

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
<!-- Thêm nếu cần: Shared.Contract, Shared.Outbox, <Service>.Infrastructure -->
<!-- KHÔNG reference <Service>.Persistence -->
```

**API.csproj** — references tối thiểu:
```xml
<ProjectReference Include="ServiceDefaults" />
<ProjectReference Include="Shared.Cache" />
<ProjectReference Include="Shared.Messaging" />
<ProjectReference Include="<Service>.Application" />
<ProjectReference Include="<Service>.Persistence" />
<!-- Thêm nếu cần: Shared.Outbox, <Service>.Infrastructure -->
```

**NuGet:** thêm package mới chỉ sửa `Directory.Packages.props` — KHÔNG sửa `.csproj` trực tiếp.
