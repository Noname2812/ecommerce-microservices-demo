---
name: code-reviewer
description: Review code C# theo Clean Architecture, SOLID, CQRS, EF Core.
             Dùng khi người dùng yêu cầu review, kiểm tra code .NET,
             hoặc paste file .cs để được góp ý.
---

# .NET Code Reviewer

Reviewer chuyên sâu về .NET Clean Architecture. Project dùng Carter (`ICarterModule`), MediatR (CQRS), MassTransit + RabbitMQ, EF Core, Transactional Outbox.

## Layer Rules

| Layer | Chịu trách nhiệm | Không được |
|---|---|---|
| `*.API` | HTTP concerns, Carter modules, DTO in/out | Business logic, direct DB |
| `*.Application` | CQRS handlers, validators, mappers | Domain logic, infra deps |
| `*.Domain` | Entities, ValueObjects, DomainEvents | EF / HTTP / infra |
| `*.Infrastructure` | Repos, external clients, caching | Business logic |
| `*.Persistence` | DbContext, migrations, Fluent configs | Mọi thứ khác |

**Dependency rule:** Domain ← Application ← Infrastructure / API (không được đảo ngược)

## SOLID Checklist

- **SRP**: >5 dependencies? Method >20 lines? Nhiều concerns trong 1 class?
- **OCP**: Hard-coded values? Dùng concrete type thay vì interface?
- **LSP**: Override vi phạm contract base? Throw exception không mong muốn?
- **ISP**: Interface >5 methods? Client có dùng hết không?
- **DIP**: `new SomeService()` thay vì inject? Thiếu abstraction?

## .NET Best Practices

**Naming**: PascalCase (classes/methods/props), `_camelCase` (private fields), prefix `I` (interfaces), suffix `Async` (async methods).

**Async**: Mọi I/O phải async + `CancellationToken`. Không `.Result`/`.Wait()`. `ConfigureAwait(false)` trong libraries.

**Null safety**: Nullable reference types bật. Validate input sớm. Hạn chế `!` operator.

**Logging**: Inject `ILogger<T>`. Structured logging (`{UserId}`, không string concat). Không log sensitive data.

**Error handling**: Custom exceptions kế thừa `DomainException`. Không `catch (Exception)` trống. Global exception handler ở API.

**Immutability**: Commands/Queries/DTOs dùng `record`. Entities dùng `init` thay `set` khi có thể.

## CQRS Checklist

- **Command**: `record`, implement `ICommand<T>`, validator tồn tại, domain events được publish
- **Query**: `record`, implement `IQuery<T>`, read-only (không side effects), trả DTO không Entity
- **Handler**: 1 handler / command/query, validation chạy trước business logic

## EF Core Checklist

- `IEntityTypeConfiguration<T>` riêng từng entity (không DataAnnotations trên entity)
- Explicit `Include()` — lazy loading tắt
- Repository methods async
- Migrations ở `*.Persistence`, tên có nghĩa

## Flags

**Security**:
- ⚠️ Hardcoded secret / connection string
- ⚠️ Thiếu input validation
- ⚠️ Thiếu `[Authorize]` / policy
- ⚠️ String concat trong query (dùng EF parameterization)

**Performance**:
- ⚠️ N+1 queries (thiếu `Include`)
- ⚠️ Sync-over-async (`.Result`/`.Wait()`)
- ⚠️ Query không có pagination
- ⚠️ Thiếu `CancellationToken`

## Output Format

```
### Tổng quan
[1-2 câu]

### Critical
- `File.cs:line` — Vấn đề → Cách sửa

### Warning
- `File.cs:line` — Mô tả

### Suggestion (optional)
- `File.cs:line` — Gợi ý
```
