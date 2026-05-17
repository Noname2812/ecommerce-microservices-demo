# Order Refactor — Task Breakdown

> Reference: [`../async-ticket-flow.md`](../async-ticket-flow.md)
> Plan approved: 2026-05-17
> Mục tiêu: hoàn thiện refactor trong 1 sprint (~2 tuần) với 3-4 dev parallel

## Team Allocation

| Team | Phạm vi |
|---|---|
| **Order** | Domain, Handler, Saga, Query, Infrastructure (Order-side), Migration, Cleanup |
| **Inventory** | ConfirmReservation command + consumer + domain method + migration |
| **Shared/Platform** | Extend contracts, MT EF Outbox registration pattern, Polly resilience template |
| **QA** | Integration tests, E2E happy path + race + idempotency checklist |

## Dependency Graph

```
TASK-01 (Contracts) ──┬──> TASK-06 (Handler async)
                      ├──> TASK-07 (Saga Normal)
                      ├──> TASK-08 (Saga Sales)
                      └──> TASK-09 (Inventory ConfirmReservation)

TASK-02 (Domain) ─────┬──> TASK-06 (Handler async)
                      ├──> TASK-07 (Saga Normal)
                      └──> TASK-08 (Saga Sales)

TASK-03 (Cleanup) ────┬──> TASK-06
                      ├──> TASK-07
                      ├──> TASK-08
                      └──> TASK-11 (MT EF Outbox)

TASK-04 (Pending slot) ──> TASK-06 (Handler) ──> TASK-10 (GetByTicket)

TASK-05 (Catalog client) ──> TASK-07 (Saga Normal validate step)
                          └─> TASK-08 (Saga Sales validate step)

TASK-09 (Inventory) ────────> TASK-07, TASK-08 (saga publish ConfirmInventory)

TASK-11 (MT EF Outbox) ─────> TASK-12 (Migration)
TASK-03 (Cleanup)      ─────> TASK-12 (Migration)
TASK-02, 04, 06-10     ─────> TASK-12 (Migration consolidate)

TASK-12 ───> TASK-13 (Integration tests)
TASK-01..13 ───> TASK-14 (Docs cleanup + reserve-vs-deduct.md)
```

## Sprint Plan (suggested)

### Sprint Week 1 (parallel)
- **Day 1-2**:
  - [TASK-01] Shared/Platform — contracts extension (S, 0.5d)
  - [TASK-02] Order — domain refactor (M, 1.5d)
  - [TASK-03] Order — delete Catalog snapshot system (L, 2d)
  - [TASK-04] Order — pending slot service (S, 1d)
  - [TASK-05] Order — Catalog client + Polly (S, 1d)
  - [TASK-09] Inventory — ConfirmReservation (M, 1.5d)
- **Day 3-5**:
  - [TASK-06] Order — Handler async (M, 1.5d) → depends on 01, 02, 04
  - [TASK-07] Order — Saga Normal refactor (L, 2.5d) → depends on 01, 02, 03, 05, 09
  - [TASK-08] Order — Saga Sales refactor (L, 2.5d) → depends on 01, 02, 03, 05, 09
  - [TASK-10] Order — GetByTicket query (S, 0.5d)

### Sprint Week 2
- **Day 6-7**:
  - [TASK-11] Order — Replace Shared.Outbox với MT EF Outbox (M, 1.5d) → depends on 03
  - [TASK-12] Order — Migration tổng hợp (M, 1d) → depends on 03, 07, 08, 11
- **Day 8-9**:
  - [TASK-13] QA — Integration tests (L, 2d) → depends on all
- **Day 10**:
  - [TASK-14] Documentation (S, 0.5d)

## Task List

| ID | Title | Team | Effort | Depends |
|---|---|---|---|---|
| [TASK-01](TASK-01-shared-contracts.md) | Extend integration events | Shared | S | — |
| [TASK-02](TASK-02-order-domain.md) | Order domain refactor | Order | M | — |
| [TASK-03](TASK-03-cleanup-catalog-snapshot.md) | Cleanup Catalog snapshot system | Order | L | — |
| [TASK-04](TASK-04-pending-slot-service.md) | Pending slot service (Redis) | Order | S | — |
| [TASK-05](TASK-05-catalog-client-resilience.md) | Catalog client + Polly resilience | Order | S | — |
| [TASK-06](TASK-06-order-handler-async.md) | Order handler async + endpoint 202 | Order | M | 01, 02, 04 |
| [TASK-07](TASK-07-saga-normal.md) | Refactor Place Order Normal Saga | Order | L | 01, 02, 03, 05, 09 |
| [TASK-08](TASK-08-saga-sales.md) | Refactor Place Sales Order Saga | Order | L | 01, 02, 03, 05, 09 |
| [TASK-09](TASK-09-inventory-confirm-reservation.md) | Inventory.ConfirmReservation | Inventory | M | 01 |
| [TASK-10](TASK-10-get-order-by-ticket.md) | GET /orders/ticket/{ticketId} | Order | S | 06 |
| [TASK-11](TASK-11-mt-ef-outbox.md) | Replace Shared.Outbox với MT EF Outbox | Order | M | 03 |
| [TASK-12](TASK-12-migration.md) | EF migration tổng hợp | Order | M | 03, 07, 08, 11 |
| [TASK-13](TASK-13-integration-tests.md) | Integration tests E2E + race + idempotency | QA | L | All |
| [TASK-14](TASK-14-documentation.md) | Docs + ADR | Order | S | All |

**Effort scale:** S=0.5–1d, M=1–2d, L=2–3d

## Definition of Done (chung)

Mỗi task hoàn thành phải:
- ✅ Code build OK (`rtk err dotnet build UrbanX.sln`)
- ✅ Unit tests cover happy + edge cases
- ✅ No warning new
- ✅ Self-review checklist (Clean Architecture, SOLID, không hardcode magic value)
- ✅ Update doc nếu có behavior mới
- ✅ PR review + approve
- ✅ Acceptance criteria pass (xem trong từng TASK file)

## Communication

- **Standup**: daily 10am, focus blocker
- **Channel**: `#order-refactor` (Slack/Teams)
- **PR labels**: `order-async-flow`, `order-cleanup`, `inventory-confirm`
- **Branch convention**: `feature/order-refactor/<task-id>-<short-desc>` (vd `feature/order-refactor/TASK-02-domain`)
