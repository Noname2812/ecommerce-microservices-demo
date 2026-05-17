# TASK-12 — EF Migration Tổng Hợp

**Team:** Order · **Effort:** M (1d) · **Depends:** TASK-03, TASK-07, TASK-08, TASK-11
**Branch:** `feature/order-refactor/TASK-12-migration`

## Mục đích

Tạo 1 migration EF Core tổng hợp tất cả schema changes của các task trước:
- DROP: bảng từ Catalog snapshot + ProcessedEvent + Shared.Outbox
- ADD: MT EF Outbox tables + saga state columns mới

Inventory side cũng cần migration riêng (TASK-09 đã có).

## Order Migration

### Generate

```bash
cd src/Services/Order/UrbanX.Order.Persistence
dotnet ef migrations add CleanupCatalogSnapshotAndOutboxRefactor
```

### Migration UP expected operations

**DROP tables (từ TASK-03 + TASK-11):**
```csharp
migrationBuilder.DropTable(name: "catalog_snapshots", schema: "read");
migrationBuilder.DropTable(name: "processed_events");
migrationBuilder.DropTable(name: "outbox_messages");
migrationBuilder.DropTable(name: "outbox_processed_events");
migrationBuilder.DropTable(name: "compensation_outbox_messages");

// DROP schema 'read' nếu rỗng
migrationBuilder.Sql("DROP SCHEMA IF EXISTS read CASCADE;");
```

**ADD MT EF Outbox tables (từ TASK-11):**
```csharp
// MT EF Outbox sẽ tự generate:
// - inbox_state (MessageId dedup tracking)
// - outbox_state (per-DbContext snapshot)
// - outbox_message (staged messages chưa publish)
```

**ADD columns cho saga state (từ TASK-07/08):**

`place_order_normal_saga_states`:
```csharp
migrationBuilder.AddColumn<string>("shipping_address_json", nullable: true);
migrationBuilder.AddColumn<decimal>("shipping_fee", precision: 18, scale: 2);
migrationBuilder.AddColumn<string>("pricing_snapshot", nullable: false, defaultValue: "{}");
migrationBuilder.AddColumn<string>("customer_email", nullable: false, defaultValue: "");
migrationBuilder.AddColumn<string>("customer_name", nullable: false, defaultValue: "");
migrationBuilder.AddColumn<string>("customer_phone", nullable: true);
migrationBuilder.AddColumn<string>("customer_note", nullable: true);
migrationBuilder.AddColumn<string>("variants_json", nullable: true);
migrationBuilder.AddColumn<string>("validation_error", nullable: true);
migrationBuilder.AddColumn<string>("payment_url", nullable: true);
migrationBuilder.AddColumn<string>("qr_code_url", nullable: true);
migrationBuilder.AddColumn<DateTimeOffset>("payment_expires_at", nullable: true);
```

`place_sales_order_saga_states` — same fields.

### Verify migration

```bash
dotnet ef migrations script --from <previous> --to CleanupCatalogSnapshotAndOutboxRefactor
```

Review SQL output:
- DROP tables không kèm theo CASCADE nếu có FK đến table khác (verify)
- ADD columns có default value đúng cho non-nullable
- MT outbox tables snake_case đúng

### Apply

```bash
dotnet ef database update
```

Verify Postgres:
```sql
\dt          -- list all tables
SELECT * FROM inbox_state LIMIT 1;
SELECT * FROM outbox_state LIMIT 1;
SELECT * FROM outbox_message LIMIT 1;
SELECT * FROM place_order_normal_saga_states LIMIT 1;  -- có columns mới
```

## Inventory Migration

### Generate (đã làm trong TASK-09)

```bash
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add AddReservationConfirmedAt
```

### Migration UP expected

```csharp
migrationBuilder.AddColumn<DateTimeOffset>(
    name: "confirmed_at",
    table: "inventory_reservations",
    type: "timestamp with time zone",
    nullable: true);
```

### Apply

```bash
dotnet ef database update
```

## Rollback Strategy

Nếu cần rollback trong dev:
```bash
# Order
cd src/Services/Order/UrbanX.Order.Persistence
dotnet ef database update <previous-migration-name>

# Inventory
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef database update <previous-migration-name>
```

Migration DOWN auto-generate sẽ recreate dropped tables (empty schema, mất data) — OK cho dev.

## Acceptance Criteria

- [ ] Migration `CleanupCatalogSnapshotAndOutboxRefactor` apply OK trên local Postgres
- [ ] Migration `AddReservationConfirmedAt` apply OK
- [ ] Verify schema:
  - DROP successful: `\dt read.*` returns nothing; `processed_events`, `outbox_messages`, `outbox_processed_events`, `compensation_outbox_messages` không exist
  - ADD successful: `inbox_state`, `outbox_state`, `outbox_message` exist với snake_case
  - Saga state tables có columns mới
  - `inventory_reservations.confirmed_at` exists
- [ ] App start OK với migration applied
- [ ] Aspire dashboard health check pass

## Notes

- **Dev project**: drop data trong các bảng cũ chấp nhận được (theo decision của user)
- **Production**: nếu deploy thực tế cần migration multi-step:
  - Step 1: deploy code dùng cả Shared.Outbox + MT EF Outbox (transition)
  - Step 2: backfill data từ outbox_messages → outbox_message
  - Step 3: deploy code chỉ dùng MT EF Outbox
  - Step 4: drop old tables
  - → Out of scope của plan này
- Verify Order migration không xung đột với migration cũ `InitialCreate` — nếu có column collision, có thể cần regenerate `InitialCreate` (last resort)

## DoD

- [ ] Migration apply trên fresh DB OK
- [ ] Migration apply trên DB hiện có (có data) OK — data trong Order/OrderItem/saga states giữ nguyên
- [ ] PR merge sau khi tất cả TASK trước merge
- [ ] Unblock TASK-13
