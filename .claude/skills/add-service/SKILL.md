---
name: add-service
description: Scaffold toàn bộ một microservice mới theo Clean Architecture (Domain + Infrastructure + Persistence + Application + API projects). Tự động invoke khi user yêu cầu "tạo service", "thêm service", "scaffold service", "tạo microservice mới", hoặc đặt tên service cụ thể như "thêm Order service", "tạo Payment service" trong hệ thống UrbanX.
allowed-tools: Read, Grep, LS, Write, Edit, MultiEdit, Bash
---
# Skill: add-service

## Khi nào dùng

Khi người dùng yêu cầu **tạo mới một service** (Order, Payment, Merchant, Inventory, v.v.) theo pattern Clean Architecture giống Catalog service.

**Trigger examples:**
- "thêm service Order"
- "tạo service Payment"
- "scaffold service Inventory"
- `/add-service`

---

## Quy trình

### Bước 1 — Xác định thông tin service

Hỏi (nếu chưa rõ):
- Tên service? (PascalCase, ví dụ: `Order`, `Payment`, `Merchant`)
- Port? (tham khảo Service Map trong CLAUDE.md)
- Có dùng **Transactional Outbox** không? (mặc định: có nếu service cần publish event)
- Có dùng **RabbitMQ/Messaging** không? (mặc định: có)
- Người dùng có cung cấp **schema database / plan migration** không?

### Bước 2 — Đọc context trước khi viết

**Reference pattern: Inventory** (Clean Architecture với Infrastructure layer đầy đủ).

**Bắt buộc đọc trước:**
- `src/AppHost/UrbanX.AppHost/AppHost.cs` — xem cách Inventory được đăng ký
- `src/Services/Inventory/UrbanX.Inventory.API/Program.cs` — template Program.cs
- `src/Services/Inventory/UrbanX.Inventory.Persistence/InventoryDbContext.cs` — template DbContext (MT Outbox entities)
- `src/Services/Inventory/UrbanX.Inventory.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` — template `AddInfrastructure()`
- `src/Services/Inventory/UrbanX.Inventory.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` — template slim `AddApplication()`

**Không đọc** Catalog (cấu trúc cũ — Application/Messaging, không có Infrastructure DI).

### Bước 3 — Thứ tự tạo file

```
1.  <Service>.Domain/           UrbanX.<Service>.Domain.csproj
                                Errors/<Service>Errors.cs   (đặt errors ở Domain — không Application)
2.  <Service>.Persistence/      UrbanX.<Service>.Persistence.csproj
                                AssemblyReference.cs
                                Constants/TableNames.cs
                                <Service>DbContext.cs       (register MT outbox entities)
                                <Service>DbContextFactory.cs
                                EfUnitOfWork.cs
                                DependencyInjection/Extensions/ServiceCollectionExtensions.cs
3.  <Service>.Application/      UrbanX.<Service>.Application.csproj
                                AssemblyReference.cs
                                DependencyInjection/Extensions/ServiceCollectionExtensions.cs  (chỉ MediatR + FluentValidation)
                                Clients/ Abstractions/      (port interfaces — handler inject)
                                Exceptions/                 (HTTP/client boundary exceptions)
4.  <Service>.Infrastructure/   UrbanX.<Service>.Infrastructure.csproj
                                DependencyInjection/Extensions/ServiceCollectionExtensions.cs  (AddInfrastructure)
                                DependencyInjection/Options/    (placeholder folder — options thêm theo feature)
                                Messaging/                  (placeholder — consumers thêm theo add-consumer skill)
                                Jobs/                       (placeholder — jobs thêm theo feature)
                                Services/                   (HTTP client / adapter impls)
5.  <Service>.API/              UrbanX.<Service>.API.csproj
                                appsettings.json
                                appsettings.Development.json
                                Abstractions/ApiEndpoint.cs
                                Apis/<Entity>Apis.cs        (placeholder Carter module)
                                Program.cs                  (gọi AddInfrastructure trước AddApplication)
6.  AppHost.cs                  Thêm đăng ký service mới
7.  UrbanX.AppHost.csproj       Thêm ProjectReference đến API project
8.  UrbanX.sln                  Chạy dotnet sln add cho 5 projects
9.  docs/<service>/             <service>-service.md
```

### Bước 4 — Không build, không run

Chỉ viết file. Sau khi viết xong toàn bộ file, thực hiện thêm 2 bước tự động:
1. Chạy `dotnet sln add` cho 5 projects vào `UrbanX.sln`
2. Thêm `ProjectReference` của API project vào `UrbanX.AppHost.csproj`

### Bước 5 — Nếu có schema database / plan migration

Sau khi scaffold xong, gọi skill **migration-generator** để tạo Domain model + Persistence config + migration.

---

## Cấu trúc file

### File 1: Domain .csproj

**Path:** `src/Services/<Service>/UrbanX.<Service>.Domain/UrbanX.<Service>.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Kernel\Shared.Kernel.csproj" />
  </ItemGroup>
</Project>
```

Tạo thêm các thư mục placeholder (không có file — dùng `.gitkeep` nếu cần):
- `Models/`
- `ValueObjects/`
- `Exceptions/`

---

### File 2: Infrastructure .csproj (ref Inventory)

**Path:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/UrbanX.<Service>.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="UrbanX.Services.<Service>.UnitTests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MassTransit.RabbitMQ" />
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\UrbanX.<Service>.Application\UrbanX.<Service>.Application.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Kernel\Shared.Kernel.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Contract\Shared.Contract.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Messaging\Shared.Messaging.csproj" />
  </ItemGroup>
</Project>
```

> Thêm `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience`, `Shared.Cache` nếu service gọi HTTP downstream.

---

### File 2b: Infrastructure — DI Extension (entry point)

**Path:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace UrbanX.<Service>.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Options/validators sẽ được thêm theo từng consumer/job/HTTP client.
        // Ví dụ (xem skill add-consumer):
        //   services.AddSingleton<IValidateOptions<XOptions>, XOptionsValidator>();
        //   services.AddOptions<XOptions>().BindConfiguration(XOptions.SectionName)
        //       .ValidateDataAnnotations().ValidateOnStart();

        // Scoped jobs / HTTP clients sẽ được thêm theo từng feature.
        //   services.AddScoped<XJob>();
        //   services.AddScoped<IXService, XService>();

        return services;
    }
}
```

> Placeholder — không thêm gì cho đến khi có consumer/job/HTTP client cụ thể.

---

### File 3: Persistence .csproj

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/UrbanX.<Service>.Persistence.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" />
    <PackageReference Include="EFCore.NamingConventions" />
    <PackageReference Include="MassTransit.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Kernel\Shared.Kernel.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
  </ItemGroup>
</Project>
```

> `MassTransit.EntityFrameworkCore` cung cấp `AddInboxStateEntity/AddOutboxMessageEntity/AddOutboxStateEntity` cho DbContext (MT EF Outbox). KHÔNG dùng `Shared.Outbox` (đã deprecated).

---

### File 4: Persistence — AssemblyReference

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/AssemblyReference.cs`

```csharp
using System.Reflection;

namespace UrbanX.<Service>.Persistence;

internal static class AssemblyReference
{
    internal static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
```

---

### File 5: Persistence — TableNames

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/Constants/TableNames.cs`

```csharp
namespace UrbanX.<Service>.Persistence.Constants;

internal static class TableNames
{
    // Table name constants sẽ được thêm theo từng entity (snake_case, số nhiều)
    // Ví dụ: internal const string Orders = "orders";
}
```

---

### File 6: DbContext (MT EF Outbox)

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/<Service>DbContext.cs`

```csharp
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace UrbanX.<Service>.Persistence;

public sealed class <Service>DbContext(DbContextOptions<<Service>DbContext> options) : DbContext(options)
{
    // DbSets sẽ được thêm khi có entity (migration-generator skill)

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // MassTransit EF Outbox entities — REQUIRED khi Program.cs dùng AddEntityFrameworkOutbox<TDbContext>
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
```

> Service **không publish event**: vẫn cần 3 `Add*Entity()` nếu chưa chắc tương lai dùng — chi phí migration nhỏ, bảo đảm tương thích.

---

### File 7: DbContextFactory

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/<Service>DbContextFactory.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrbanX.<Service>.Persistence;

public sealed class <Service>DbContextFactory : IDesignTimeDbContextFactory<<Service>DbContext>
{
    public <Service>DbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__<servicedb>")
            ?? "Host=localhost;Port=5432;Database=urbanx_<service_lowercase>;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<<Service>DbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new <Service>DbContext(options);
    }
}
```

> Thay `<servicedb>` bằng tên connection string dùng trong AppHost (ví dụ: `orderdb`).  
> Thay `<service_lowercase>` bằng tên service viết thường (ví dụ: `urbanx_order`).

---

### File 8: Persistence — DI Extension

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        // Repository registrations sẽ được thêm theo từng entity
        // services.AddScoped<I<Entity>Repository, <Entity>Repository>();
        return services;
    }
}
```

### File 8b: Persistence — EfUnitOfWork

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/EfUnitOfWork.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Persistence;

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
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
```

---

### File 9: Application .csproj

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/UrbanX.<Service>.Application.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="UrbanX.Services.<Service>.UnitTests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Application\Shared.Application.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Contract\Shared.Contract.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
  </ItemGroup>
</Project>
```

> KHÔNG include `Shared.Messaging`, `MassTransit.*` — consumer giờ ở Infrastructure. KHÔNG reference Persistence hoặc Infrastructure.

---

### File 10: Application — AssemblyReference

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/AssemblyReference.cs`

```csharp
using System.Reflection;

namespace UrbanX.<Service>.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
```

---

### File 11: Domain — Errors placeholder (đặt tại Domain, KHÔNG Application)

**Path:** `src/Services/<Service>/UrbanX.<Service>.Domain/Errors/<Service>Errors.cs`

```csharp
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Domain.Errors;

public static class <Service>Errors
{
    // Errors sẽ được thêm theo từng use case
    // Ví dụ:
    // public static Error NotFound(Guid id) =>
    //     new("<Entity>.NotFound", $"<Entity> {id} not found");
}
```

> Lý do: error code là phần của business contract; Domain là nguồn chân lý. Application chỉ chứa `Application/Exceptions/` cho HTTP/client boundary exceptions.

---

### File 12: Application — DI Extension (slim — chỉ MediatR + FluentValidation)

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;

namespace UrbanX.<Service>.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
```

> Đây là dạng cuối cùng. KHÔNG register options, repository, consumer, processor, HTTP client ở đây — toàn bộ thuộc `AddInfrastructure()`.

---

### File 14: API .csproj

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/UrbanX.<Service>.API.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Asp.Versioning.Mvc.ApiExplorer" />
    <PackageReference Include="Carter" />
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <!-- Hangfire (nếu service cần recurring job — Inventory dùng cho TTL release) -->
    <!-- <PackageReference Include="Hangfire.AspNetCore" /> -->
    <!-- <PackageReference Include="Hangfire.InMemory" /> -->
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\ServiceDefaults\UrbanX.ServiceDefaults\UrbanX.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Cache\Shared.Cache.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Application\UrbanX.<Service>.Application.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Infrastructure\UrbanX.<Service>.Infrastructure.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Persistence\UrbanX.<Service>.Persistence.csproj" />
  </ItemGroup>
</Project>
```

> Authentication JwtBearer KHÔNG cần — Trust-Gateway pattern (Gateway verify JWT, service chỉ đọc headers).

---

### File 15: appsettings.json

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "IdentityServer": {
    "Audience": "urbanx-api"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Username": "guest",
    "Password": "guest"
  }
}
```

---

### File 16: appsettings.Development.json

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```

---

### File 17: ApiEndpoint base class

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/Abstractions/ApiEndpoint.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static IResult HandleFailure(Result result) => result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", 400, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails("Bad Request", 400, result.Error))
    };

    protected static IResult To<Service>Result(Result result)
    {
        if (result is IValidationResult)
            return HandleFailure(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("Expected a failed result.");

        var status = result.Error.Code switch
        {
            var c when c.EndsWith("NotFound") => StatusCodes.Status404NotFound,
            "FORBIDDEN" => StatusCodes.Status403Forbidden,
            "OPTIMISTIC_LOCK_CONFLICT" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

    protected static IResult To<Service>Result<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : To<Service>Result((Result)result);

    private static ProblemDetails CreateProblemDetails(
        string title, int status, Error error, Error[]? errors = null) => new()
    {
        Title = title,
        Type = error.Code,
        Detail = error.Message,
        Status = status,
        Extensions = { { nameof(errors), errors } }
    };
}
```

> Thay `To<Service>Result` bằng tên thực (ví dụ: `ToOrderResult`, `ToPaymentResult`).

---

### File 18: Placeholder Carter Module

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/Apis/<Entity>Apis.cs`

Tạo file này với tên entity chính của service (ví dụ: `OrderApis.cs`, `PaymentApis.cs`):

```csharp
using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.<Service>.API.Abstractions;

namespace UrbanX.<Service>.API.Apis;

public class <Entity>Apis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/<service_lowercase>/<entities>";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("<Entity>")
            .MapGroup(BaseURL).HasApiVersion(1);

        // Endpoints sẽ được thêm theo từng use case (add-command / add-query skill)
    }
}
```

> URL pattern: `/api/v1/<service_lowercase>/<entities>` (ví dụ: `/api/v1/orders`, `/api/v1/payments`).

---

### File 19: Program.cs

**Path:** `src/Services/<Service>/UrbanX.<Service>.API/Program.cs`

```csharp
using Carter;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.<Service>.Application.DependencyInjection.Extensions;
using UrbanX.<Service>.Infrastructure.DependencyInjection.Extensions;
using UrbanX.<Service>.Persistence;
using UrbanX.<Service>.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

// Database (snake_case naming convention)
builder.AddNpgsqlDbContext<<Service>DbContext>("<servicedb>",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

// Infrastructure: register options/consumers/jobs/clients BEFORE MassTransit resolves ConsumerDefinitions.
builder.Services.AddInfrastructure();

// Application: MediatR + FluentValidation
builder.Services.AddApplication();

// MassTransit + EF Outbox: declare consumers ở đây (Definition đã có DI từ AddInfrastructure)
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddEntityFrameworkOutbox<<Service>DbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
        });

        // bus.AddConsumer<XConsumer>(typeof(XConsumerDefinition));  // theo add-consumer skill
    });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<<Service>DbContext>(name: "<servicedb>", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Persistence
builder.Services.AddPersistence();

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

// Optional — Hangfire (nếu service cần recurring jobs):
// builder.Services.AddHangfire(config => config.UseInMemoryStorage());
// builder.Services.AddHangfireServer();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();

// Trust-the-Gateway: read identity from X-User-* headers (set by Gateway).
app.UseUserContext();

// Auto-migrate
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<<Service>DbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for <Service>DbContext...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}

app.MapCarter();
app.Run();

public partial class Program { }
```

> Thay `<servicedb>` bằng connection string name (ví dụ: `"orderdb"`).  
> Thứ tự chốt: **AddNpgsqlDbContext → AddInfrastructure → AddApplication → AddConfigMessaging + AddMessaging → AddPersistence → AddCarter**.

---

### File 20: Đăng ký AppHost

**Sửa file:** `src/AppHost/UrbanX.AppHost/AppHost.cs`

```csharp
// 1. Tạo database resource
var <servicedb> = postgres.AddDatabase("<servicedb>", "urbanx_<service_lowercase>");

// 2. Đăng ký service project
var <serviceVariable> = builder.AddProject<Projects.UrbanX_<Service>_API>("<service_lowercase>")
    .WithReference(<servicedb>)
    .WithReference(rabbitMq)
    .WaitFor(<servicedb>)
    .WaitFor(rabbitMq);

// 3. Thêm reference vào Gateway
gateway
    .WithReference(<serviceVariable>)
    .WaitFor(<serviceVariable>);
```

> Convention đặt tên biến: camelCase (ví dụ: `orderService`, `paymentService`, `orderDb`).  
> Nếu service không cần RabbitMQ: bỏ `.WithReference(rabbitMq)` và `.WaitFor(rabbitMq)`.

---

### File 20.5: Thêm ProjectReference vào AppHost

**Sửa file:** `src/AppHost/UrbanX.AppHost/UrbanX.AppHost.csproj`

Thêm dòng sau vào `<ItemGroup>` chứa các `ProjectReference`:

```xml
<ProjectReference Include="..\..\Services\<Service>\UrbanX.<Service>.API\UrbanX.<Service>.API.csproj" />
```

Aspire cần ProjectReference này để generate type-safe `Projects.UrbanX_<Service>_API` dùng trong `AppHost.cs`.

---

## Sau khi scaffold xong

### Thêm vào solution (Claude tự chạy)

Chạy các lệnh sau để thêm 5 projects vào `UrbanX.sln`:

```bash
dotnet sln UrbanX.sln add src/Services/<Service>/UrbanX.<Service>.Domain/UrbanX.<Service>.Domain.csproj
dotnet sln UrbanX.sln add src/Services/<Service>/UrbanX.<Service>.Infrastructure/UrbanX.<Service>.Infrastructure.csproj
dotnet sln UrbanX.sln add src/Services/<Service>/UrbanX.<Service>.Persistence/UrbanX.<Service>.Persistence.csproj
dotnet sln UrbanX.sln add src/Services/<Service>/UrbanX.<Service>.Application/UrbanX.<Service>.Application.csproj
dotnet sln UrbanX.sln add src/Services/<Service>/UrbanX.<Service>.API/UrbanX.<Service>.API.csproj
```

### Nếu có schema database

Tiếp tục dùng skill **migration-generator** để tạo:
- Domain entities và repository interfaces
- EF Core configurations
- Persistence registrations
- Nhắc chạy `dotnet ef migrations add InitialCreate`

### Gateway route (nếu cần path mới)

Thêm vào `src/Gateway/UrbanX.Gateway/appsettings.json`:

```json
{
  "RouteId": "<service>-route",
  "Match": { "Path": "/api/v{version:apiVersion}/<service_lowercase>/{**catch-all}" },
  "Transforms": [{ "PathPattern": "/api/v{version:apiVersion}/<service_lowercase>/{**catch-all}" }],
  "ClusterId": "<service_lowercase>-cluster"
}
```

---

## Checklist trước khi xong

- [ ] 5 project files (.csproj) đúng references theo template Inventory
- [ ] `Application.csproj` KHÔNG có `MassTransit.RabbitMQ`, KHÔNG có `Shared.Messaging` reference
- [ ] `Infrastructure.csproj` có `MassTransit.RabbitMQ` + `MediatR` + `Shared.Messaging` + `Shared.Contract`
- [ ] `AssemblyReference.cs` có trong cả Application và Persistence
- [ ] `EfUnitOfWork` trong Persistence implement `IUnitOfWork` dùng `<Service>DbContext`
- [ ] `AddPersistence()` register `IUnitOfWork` + repositories
- [ ] `<Service>DbContext` kế thừa `DbContext` và **gọi 3 `Add*Entity()`** của MT trong `OnModelCreating`
- [ ] `<Service>DbContextFactory` dùng đúng connection string name và DB name
- [ ] `Program.cs` thứ tự: **AddNpgsqlDbContext → AddInfrastructure → AddApplication → AddConfigMessaging+AddMessaging → AddPersistence → AddCarter**
- [ ] `AddApplication()` chỉ gọi `AddMediatorWithPielineDefault(AssemblyReference.Assembly)` (1 dòng)
- [ ] `AddInfrastructure()` tồn tại ở `Infrastructure/DependencyInjection/Extensions/`
- [ ] Errors đặt tại `Domain/Errors/<Service>Errors.cs` (KHÔNG Application/Usecases/V1/Errors/)
- [ ] AppHost đăng ký đúng: database resource → service project → gateway reference (kèm `.WithReference(rabbitMq)`/`.WithReference(redis)`)
- [ ] `UrbanX.AppHost.csproj` đã có `ProjectReference` đến API project
- [ ] Placeholder Carter module tạo đúng URL pattern
- [ ] `ApiEndpoint.cs` có `To<Service>Result` tên đúng theo service
- [ ] Đã chạy `dotnet sln add` cho 5 projects (Claude tự làm)
- [ ] Doc: tạo `docs/<service_lowercase>/<service_lowercase>-service.md`

---

## Ví dụ đầy đủ — Scaffold Order service

### Biến thay thế

| Placeholder | Giá trị |
|---|---|
| `<Service>` | `Order` |
| `<service_lowercase>` | `order` |
| `<servicedb>` | `orderdb` |
| `<Entity>` | `Order` |
| `<entities>` | `orders` |
| `<serviceVariable>` | `orderService` |

### AppHost snippet

```csharp
var orderDb = postgres.AddDatabase("orderdb", "urbanx_order");

var orderService = builder.AddProject<Projects.UrbanX_Order_API>("order")
    .WithReference(orderDb)
    .WithReference(rabbitMq)
    .WaitFor(orderDb)
    .WaitFor(rabbitMq);

gateway
    .WithReference(orderService)
    .WaitFor(orderService);
```

### AppHost.csproj — thêm ProjectReference

```xml
<ProjectReference Include="..\..\Services\Order\UrbanX.Order.API\UrbanX.Order.API.csproj" />
```

### dotnet sln commands (Claude tự chạy)
```bash
dotnet sln UrbanX.sln add `
  --solution-folder "src/Services/Order" `
  src/Services/Order/UrbanX.Order.API/UrbanX.Order.API.csproj `
  src/Services/Order/UrbanX.Order.Application/UrbanX.Order.Application.csproj `
  src/Services/Order/UrbanX.Order.Domain/UrbanX.Order.Domain.csproj `
  src/Services/Order/UrbanX.Order.Infrastructure/UrbanX.Order.Infrastructure.csproj `
  src/Services/Order/UrbanX.Order.Persistence/UrbanX.Order.Persistence.csproj
```
