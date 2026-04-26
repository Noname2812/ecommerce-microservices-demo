---
name: migration-generator
description: Tạo migration cho các thay đổi trong model (thêm entity, thêm field, sửa field, xóa entity). Tự động invoke khi user yêu cầu "tạo migration", "thêm migration", "scaffold migration", hoặc bất kỳ thay đổi nào liên quan đến database schema trong hệ thống UrbanX.
allowed-tools: Read, Grep, LS, Write, Edit, MultiEdit, Bash
---
# Skill: migration-generator

## Khi nào dùng

Khi người dùng yêu cầu **thêm model mới**, **chỉnh sửa model hiện có**, hoặc **xóa model** trong bất kỳ service nào. Skill này xử lý phần Domain + Persistence (model, config, repository, DI) — không tạo command/query.

**Trigger examples:**
- "thêm model Tag vào Catalog"
- "thêm field WeightGrams vào Product"
- "xóa entity VariantSkuHistory"
- "tạo entity Review với IReviewRepository"
- `/migration-generator`

---

## Quy trình

### Bước 1 — Xác định thay đổi

Hỏi (nếu chưa rõ):
- Service nào? (Catalog, Order, Payment, ...)
- Thay đổi gì? (thêm entity mới / thêm field / sửa field / xóa entity / thêm enum)
- Entity mới có cần IRepository không? (mặc định: có nếu command handler sẽ dùng trực tiếp)
- Quan hệ với entity khác? (FK, navigation property)

### Bước 2 — Đọc context trước khi viết

**Bắt buộc đọc trước:**
- Các entity liên quan: `src/Services/<Service>/<Service>.Domain/Models/`
- `TableNames.cs`: `src/Services/<Service>/<Service>.Persistence/Constants/TableNames.cs`
- `CatalogDbContext.cs` (hoặc DbContext của service): để biết DbSet hiện có
- `ServiceCollectionExtensions.cs` trong Persistence: để biết DI đã đăng ký gì
- Nếu sửa entity có sẵn: đọc file Configuration tương ứng trong `Configurations/`

**Không đọc** các service khác trừ khi được yêu cầu rõ ràng.

### Bước 3 — Thứ tự tạo/sửa file

```
1. Domain/Models/<Entity>.cs          ← entity class
2. Domain/ValueObjects/<Name>.cs      ← enum/value object (nếu cần)
3. Domain/I<Entity>Repository.cs      ← interface (không có method)
4. Persistence/Constants/TableNames.cs ← thêm table name constant
5. Persistence/Configurations/<Entity>Configuration.cs  ← EF config
6. Persistence/CatalogDbContext.cs    ← thêm DbSet
7. Persistence/<Entity>Repository.cs  ← implementation (không có method)
8. Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs ← đăng ký DI
9. docs/<service>/migrations/<entity>.md  ← doc
```

### Bước 4 — Không build, không run

Chỉ viết file. Không chạy `dotnet ef migrations add` hay `dotnet build` trừ khi người dùng yêu cầu.

---

## Cấu trúc file

### File 1: Entity Model

**Path:** `src/Services/<Service>/<Service>.Domain/Models/<Entity>.cs`

```csharp
using Shared.Kernel.Domain;

namespace UrbanX.<Service>.Domain.Models
{
    public class <Entity> : BaseEntity<Guid>
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        // DateTimeOffset? DeletedAt  ← thêm nếu entity cần soft delete
        // int RowVersion             ← thêm nếu entity cần optimistic concurrency
    }
}
```

**Lưu ý:**
- Kế thừa `BaseEntity<Guid>` từ `Shared.Kernel.Domain` — đã có sẵn `Id` (Guid)
- Properties là `{ get; set; }` (không phải `init`) để EF Core proxy hoạt động đúng
- Navigation properties: dùng `null!` cho required, `?` cho optional
- Không inject bất kỳ service nào vào entity
- Soft delete: thêm `DateTimeOffset? DeletedAt` nếu cần (không dùng ISoftDelete interface riêng)
- Concurrency: thêm `int RowVersion` nếu entity có thể bị edit đồng thời

---

### File 2: Value Object / Enum (nếu cần)

**Khi nào tạo:**
- Entity có field dạng string với tập giá trị cố định → dùng static constants (không dùng C# enum để tránh migration phức tạp khi DB lưu string)

**Path:** `src/Services/<Service>/<Service>.Domain/ValueObjects/<Name>.cs`

```csharp
namespace UrbanX.<Service>.Domain.ValueObjects
{
    // Dùng static class + string constants thay vì C# enum
    // Lý do: DB lưu string, dễ thêm giá trị mới mà không cần migration phức tạp
    public static class <Name>
    {
        public const string Active = "ACTIVE";
        public const string Inactive = "INACTIVE";
        public const string Draft = "DRAFT";
    }
}
```

**Lưu ý:**
- Dùng UPPER_CASE cho status values (theo pattern `ProductStatus`)
- Dùng lowercase cho type values (theo pattern `AttributeValueTypes`: `"text"`, `"number"`)

---

### File 3: Repository Interface

**Path:** `src/Services/<Service>/<Service>.Domain/I<Entity>Repository.cs`

```csharp
namespace UrbanX.<Service>.Domain
{
    // Interface trống — methods sẽ được thêm khi có command/query cần dùng
    public interface I<Entity>Repository
    {
    }
}
```

**Lưu ý:**
- Interface trống là đúng theo yêu cầu skill — phương thức sẽ được thêm theo từng use case
- Namespace: `UrbanX.<Service>.Domain` (không phải `.Models` hay `.Repositories`)

---

### File 4: TableNames constant

**Sửa file:** `src/Services/<Service>/<Service>.Persistence/Constants/TableNames.cs`

```csharp
// Thêm constant mới theo snake_case PostgreSQL convention
internal const string <Entities> = "<table_name>";
```

**Convention:** `snake_case`, số nhiều (ví dụ: `"product_reviews"`, `"tags"`)

---

### File 5: EF Core Configuration

**Path:** `src/Services/<Service>/<Service>.Persistence/Configurations/<Entity>Configuration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.<Service>.Domain.Models;
using UrbanX.<Service>.Persistence.Constants;

namespace UrbanX.<Service>.Persistence.Configurations
{
    internal sealed class <Entity>Configuration : IEntityTypeConfiguration<<Entity>>
    {
        public void Configure(EntityTypeBuilder<<Entity>> builder)
        {
            builder.ToTable(TableNames.<Entities>);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();

            builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
            builder.HasIndex(x => x.Name).IsUnique(); // nếu cần unique

            // Decimal: luôn khai báo precision
            builder.Property(x => x.Price).HasPrecision(18, 2);

            // String enum/status
            builder.Property(x => x.Status).HasMaxLength(20);
            builder.HasIndex(x => x.Status);

            // FK relationship
            builder.HasOne<ParentEntity>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade); // hoặc Restrict, SetNull

            // Concurrency token
            builder.Property(x => x.RowVersion).IsConcurrencyToken();

            // Default value
            builder.Property(x => x.IsActive).HasDefaultValue(true);
        }
    }
}
```

**Rules:**
- Class là `internal sealed`
- `ValueGeneratedNever()` cho tất cả PK (app tự assign GUID)
- Decimal: `HasPrecision(18, 2)` — bắt buộc cho price/amount
- String: `HasMaxLength(...)` — luôn khai báo
- Không dùng data annotations trên entity class — chỉ dùng Fluent API

---

### File 6: Cập nhật DbContext

**Sửa file:** `src/Services/<Service>/<Service>.Persistence/<Service>DbContext.cs`

```csharp
// Thêm DbSet vào DbContext (giữ thứ tự alphabetical hoặc theo nhóm logic)
public DbSet<<Entity>> <Entities> => Set<<Entity>>();
```

**Lưu ý:**
- Format: `public DbSet<T> <Name> => Set<T>();` (expression-body, không phải auto-property)
- Config được pick up tự động qua `ApplyConfigurationsFromAssembly()` — không cần gọi thủ công

---

### File 7: Repository Implementation

**Path:** `src/Services/<Service>/<Service>.Persistence/<Entity>Repository.cs`

```csharp
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Persistence
{
    // Implementation trống — methods sẽ được thêm theo từng use case
    public sealed class <Entity>Repository : I<Entity>Repository
    {
        private readonly <Service>DbContext _db;

        public <Entity>Repository(<Service>DbContext db) => _db = db;
    }
}
```

**Lưu ý:**
- Class là `public sealed`
- Constructor dùng primary expression: `=> _db = db`
- Namespace: `UrbanX.<Service>.Persistence` (không có `.Repositories` subfolder — theo pattern hiện tại)

---

### File 8: Đăng ký DI

**Sửa file:** `src/Services/<Service>/<Service>.Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`

```csharp
// Thêm dòng đăng ký mới vào AddPersistence()
services.AddScoped<I<Entity>Repository, <Entity>Repository>();
```

**Lưu ý:**
- Lifetime: `AddScoped` (EF DbContext là scoped)
- Đặt theo thứ tự alphabetical hoặc theo nhóm với entity liên quan

---

## Sửa entity hiện có

### Thêm field mới

1. Thêm property vào Domain model
2. Thêm config vào `<Entity>Configuration.cs` (maxLength, precision, index nếu cần)
3. Nhắc người dùng chạy `dotnet ef migrations add <Name>`

### Xóa field

1. Xóa property khỏi Domain model
2. Xóa config tương ứng
3. Nếu field có index: xóa `HasIndex()` call
4. Nhắc người dùng chạy `dotnet ef migrations add <Name>`

### Xóa entity hoàn toàn

**Phải hỏi xác nhận trước** (theo response-rules.md). Sau khi được xác nhận:
1. Xóa Domain model file
2. Xóa Configuration file
3. Xóa IRepository interface
4. Xóa Repository implementation
5. Xóa DbSet khỏi DbContext
6. Xóa TableNames constant
7. Xóa DI registration
8. Nhắc người dùng chạy `dotnet ef migrations add Remove<Entity>`

---

## Checklist trước khi xong

- [ ] Entity kế thừa `BaseEntity<Guid>`, properties dùng `{ get; set; }`
- [ ] Value object dùng static class + string constants (không dùng C# enum)
- [ ] Interface trống, namespace `UrbanX.<Service>.Domain`
- [ ] TableName là `snake_case`, số nhiều, thêm vào `TableNames.cs`
- [ ] Configuration là `internal sealed class`, dùng `ValueGeneratedNever()`
- [ ] Decimal fields có `HasPrecision(18, 2)`
- [ ] String fields có `HasMaxLength(...)`
- [ ] DbSet thêm vào DbContext: `public DbSet<T> Name => Set<T>();`
- [ ] Repository là `public sealed class`, namespace `UrbanX.<Service>.Persistence`
- [ ] DI đăng ký `AddScoped<IRepo, Repo>()` trong `ServiceCollectionExtensions`
- [ ] Doc: tạo `docs/<service>/migrations/<entity>.md`
- [ ] Nhắc người dùng chạy EF migration command

---

## EF Migration command

Nhắc người dùng chạy sau khi tất cả file đã được tạo:

```bash
cd src/Services/<Service>/UrbanX.<Service>.Persistence
dotnet ef migrations add <MigrationName>
# Ví dụ: dotnet ef migrations add AddTagEntity
# Ví dụ: dotnet ef migrations add AddWeightGramsToProduct
```

Migration name convention:
- Thêm entity mới: `Add<Entity>`
- Thêm field: `Add<FieldName>To<Entity>`
- Xóa entity: `Remove<Entity>`
- Sửa schema: `<DescribeChange>`

---

## Ví dụ đầy đủ — Thêm entity Tag vào Catalog

### Domain model

```csharp
// src/Services/Catalog/UrbanX.Catalog.Domain/Models/Tag.cs
using Shared.Kernel.Domain;

namespace UrbanX.Catalog.Domain.Models
{
    public class Tag : BaseEntity<Guid>
    {
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public bool IsActive { get; set; } = true;
    }
}
```

### Interface

```csharp
// src/Services/Catalog/UrbanX.Catalog.Domain/ITagRepository.cs
namespace UrbanX.Catalog.Domain
{
    public interface ITagRepository
    {
    }
}
```

### TableNames

```csharp
// Thêm vào Constants/TableNames.cs
internal const string Tags = "tags";
```

### Configuration

```csharp
// src/Services/Catalog/UrbanX.Catalog.Persistence/Configurations/TagConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
    {
        public void Configure(EntityTypeBuilder<Tag> builder)
        {
            builder.ToTable(TableNames.Tags);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();

            builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => x.Name).IsUnique();
            builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => x.Slug).IsUnique();
            builder.Property(x => x.IsActive).HasDefaultValue(true);
        }
    }
}
```

### DbContext

```csharp
// Thêm vào CatalogDbContext.cs
public DbSet<Tag> Tags => Set<Tag>();
```

### Repository

```csharp
// src/Services/Catalog/UrbanX.Catalog.Persistence/TagRepository.cs
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Persistence
{
    public sealed class TagRepository : ITagRepository
    {
        private readonly CatalogDbContext _db;

        public TagRepository(CatalogDbContext db) => _db = db;
    }
}
```

### DI

```csharp
// Thêm vào ServiceCollectionExtensions.cs AddPersistence()
services.AddScoped<ITagRepository, TagRepository>();
```

### Migration command

```bash
cd src/Services/Catalog/UrbanX.Catalog.Persistence
dotnet ef migrations add AddTagEntity
```
