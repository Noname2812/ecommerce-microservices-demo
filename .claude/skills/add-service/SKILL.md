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

**Bắt buộc đọc trước:**
- `src/AppHost/UrbanX.AppHost/AppHost.cs` — xem cách Catalog được đăng ký để làm theo
- `src/Services/Catalog/UrbanX.Catalog.API/Program.cs` — template Program.cs
- `src/Services/Catalog/UrbanX.Catalog.Persistence/CatalogDbContext.cs` — template DbContext

**Không đọc** toàn bộ Catalog service — chỉ đọc 3 file trên là đủ.

### Bước 3 — Thứ tự tạo file

```
1.  <Service>.Domain/           UrbanX.<Service>.Domain.csproj
2.  <Service>.Infrastructure/   UrbanX.<Service>.Infrastructure.csproj
3.  <Service>.Persistence/      UrbanX.<Service>.Persistence.csproj
                                AssemblyReference.cs
                                Constants/TableNames.cs
                                <Service>DbContext.cs
                                <Service>DbContextFactory.cs
                                DependencyInjection/Extensions/ServiceCollectionExtensions.cs
4.  <Service>.Application/      UrbanX.<Service>.Application.csproj
                                AssemblyReference.cs
                                Behaviors/<Service>TransactionBehavior.cs
                                Usecases/V1/Errors/<Service>Errors.cs
                                DependencyInjection/Extensions/ServiceCollectionExtensions.cs
5.  <Service>.API/              UrbanX.<Service>.API.csproj
                                appsettings.json
                                appsettings.Development.json
                                Abstractions/ApiEndpoint.cs
                                Apis/<Entity>Apis.cs           (placeholder Carter module)
                                Program.cs
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

### File 2: Infrastructure .csproj

**Path:** `src/Services/<Service>/UrbanX.<Service>.Infrastructure/UrbanX.<Service>.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

> Placeholder — để trống. Dùng khi cần integrate external services (Stripe, v.v.)

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
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Kernel\Shared.Kernel.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Outbox\Shared.Outbox.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
  </ItemGroup>
</Project>
```

> Nếu service **không dùng Outbox**: xóa `Shared.Outbox` reference.

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

### File 6: DbContext

**Path:** `src/Services/<Service>/UrbanX.<Service>.Persistence/<Service>DbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;

namespace UrbanX.<Service>.Persistence;

public sealed class <Service>DbContext(DbContextOptions<<Service>DbContext> options) : OutboxDbContext(options)
{
    // DbSets sẽ được thêm khi có entity (migration-generator skill)

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
```

> Nếu service **không dùng Outbox**: kế thừa `DbContext` thay vì `OutboxDbContext`, và xóa `using Shared.Outbox.EfCore`.

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

namespace UrbanX.<Service>.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // Repository registrations sẽ được thêm theo từng entity
        // services.AddScoped<I<Entity>Repository, <Entity>Repository>();
        return services;
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
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Application\Shared.Application.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Messaging\Shared.Messaging.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Domain\UrbanX.<Service>.Domain.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Persistence\UrbanX.<Service>.Persistence.csproj" />
  </ItemGroup>
</Project>
```

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

### File 11: TransactionBehavior

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/Behaviors/<Service>TransactionBehavior.cs`

```csharp
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Messaging.Behaviors;
using UrbanX.<Service>.Persistence;

namespace UrbanX.<Service>.Application.Behaviors;

internal sealed class <Service>TransactionBehavior<TRequest, TResponse>(
    <Service>DbContext dbContext,
    ILogger<<Service>TransactionBehavior<TRequest, TResponse>> logger)
    : TransactionPipelineBehavior<TRequest, TResponse, <Service>DbContext>(dbContext, logger)
    where TRequest : ICommandBase
    where TResponse : notnull;
```

---

### File 12: Application — Errors placeholder

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/Usecases/V1/Errors/<Service>Errors.cs`

```csharp
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Application.Usecases.V1.Errors;

public static class <Service>Errors
{
    // Errors sẽ được thêm theo từng use case
    // Ví dụ:
    // public static Error NotFound(Guid id) =>
    //     new("<Entity>.NotFound", $"<Entity> {id} not found");
}
```

---

### File 13: Application — DI Extension

**Path:** `src/Services/<Service>/UrbanX.<Service>.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.<Service>.Application.Behaviors;
using UrbanX.<Service>.Persistence.DependencyInjection.Extensions;

namespace UrbanX.<Service>.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(
            assembly: AssemblyReference.Assembly,
            cfg =>
            {
                cfg.AddOpenBehavior(typeof(<Service>TransactionBehavior<,>));
        });
        services.AddPersistence();
        return services;
    }
}
```

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
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\ServiceDefaults\UrbanX.ServiceDefaults\UrbanX.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Messaging\Shared.Messaging.csproj" />
    <ProjectReference Include="..\..\..\Shared\Shared.Outbox\Shared.Outbox.csproj" />
    <ProjectReference Include="..\UrbanX.<Service>.Application\UrbanX.<Service>.Application.csproj" />
  </ItemGroup>
</Project>
```

> Nếu service **không dùng Outbox**: xóa `Shared.Outbox` reference.

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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.<Service>.Application.DependencyInjection.Extensions;
using UrbanX.<Service>.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<<Service>DbContext>("<servicedb>");
builder.Services.AddOutbox<<Service>DbContext>(
    configureDb: null,
    builder.Configuration
);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<<Service>DbContext>(name: "<servicedb>", tags: ["ready", "db"]);

// JWT auth
var identityAuthority = builder.Configuration["services__identity__https__0"]
    ?? builder.Configuration["services__identity__http__0"]
    ?? builder.Configuration["IdentityServer:Authority"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityAuthority;
        options.Audience = builder.Configuration["IdentityServer:Audience"] ?? "urbanx-api";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication(builder.Configuration);

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// Auto-migrate on startup
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

> Nếu service **không dùng Outbox**: xóa block `AddOutbox<...>`.  
> Nếu service **không dùng Messaging**: xóa block `AddConfigMessaging` + `AddMessaging`.  
> Thay `<servicedb>` bằng connection string name (ví dụ: `"orderdb"`).

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

- [ ] 5 project files (.csproj) đúng references — đặc biệt Outbox ref nếu dùng
- [ ] `AssemblyReference.cs` có trong cả Application và Persistence
- [ ] `<Service>TransactionBehavior` kế thừa `TransactionPipelineBehavior<TRequest, TResponse, TDbContext>`
- [ ] `<Service>DbContext` kế thừa `OutboxDbContext` (hoặc `DbContext` nếu không dùng Outbox)
- [ ] `<Service>DbContextFactory` dùng đúng connection string name và DB name
- [ ] `Program.cs` dùng đúng connection string name (khớp với AppHost)
- [ ] `AddApplication()` gọi `AddMediator` + TransactionBehavior + `AddPersistence`
- [ ] AppHost đăng ký đúng: database resource → service project → gateway reference
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
