# Tasks — Saga Orchestration Migration

Mỗi file dưới đây là **1 task độc lập đủ context** để giao cho member.

> Đọc trước: [../saga-orchestration-plan.md](../saga-orchestration-plan.md) cho overview kiến trúc.

## Danh sách task

| ID | Tên | Effort | Owner gợi ý | Depends |
|---|---|---|---|---|
| [TASK-01](TASK-01-saga-contract-events.md) | Saga Contract Events (Shared.Contract) | 0.5d | Dev A | — |
| [TASK-02](TASK-02-saga-state-machine.md) | Saga State + State Machine + Migration | 2d | Dev A | TASK-01 |
| [TASK-03](TASK-03-promotion-consumers.md) | Promotion Service Consumers | 1.5d | Dev B | TASK-01 |
| [TASK-04](TASK-04-inventory-consumer.md) | Inventory Service Consumer | 1d | Dev C | TASK-01 |
| [TASK-05](TASK-05-sales-api-refactor.md) | PlaceSalesOrder API Refactor (202 Accepted) | 2d | Dev A | TASK-02, 03, 04 |
| [TASK-06](TASK-06-hot-path-optimizations.md) | Hot-path Optimizations | 1.5d | Dev D | — |
| [TASK-07](TASK-07-normal-hardening.md) | PlaceOrder Normal Hardening (Polly + atomic Coupon) | 1d | Dev C | — |
| [TASK-08](TASK-08-documentation.md) | Documentation (8 docs) | 1d | Dev D | all |

## Task graph

```
TASK-01 ──┬── TASK-02 ──┐
          ├── TASK-03 ──┼── TASK-05 ── TASK-08
          └── TASK-04 ──┘                ▲
                                         │
TASK-06 ────────────────────────────────┤
TASK-07 ────────────────────────────────┘
```

## Conventions chung khi pick-up task

1. **Branch**: `feat/saga/<task-id>-<short-name>` (vd `feat/saga/task-02-state-machine`).
2. **Commit**: theo style hiện tại (`Add:`, `Fix:`, `Update:`). Reference `TASK-XX` trong PR description.
3. **Build trước khi push**: `dotnet build UrbanX.sln`.
4. **Unit test**: bắt buộc cover acceptance criteria của task.
5. **Doc**: mỗi task khi hoàn thành ghi 1 đoạn note kỹ thuật ngắn (file gist hoặc PR description) — Dev D (TASK-08) tổng hợp thành docs cuối.

## Status tracking

- [ ] TASK-01 — Saga Contract Events
- [ ] TASK-02 — Saga State + State Machine + Migration
- [ ] TASK-03 — Promotion Service Consumers
- [ ] TASK-04 — Inventory Service Consumer
- [ ] TASK-05 — PlaceSalesOrder API Refactor
- [ ] TASK-06 — Hot-path Optimizations
- [ ] TASK-07 — PlaceOrder Normal Hardening
- [ ] TASK-08 — Documentation
