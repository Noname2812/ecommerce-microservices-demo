# TASK-08 · Documentation (8 final docs trong `/docs/order/`)

| | |
|---|---|
| **Effort** | ~1 ngày |
| **Depends on** | TASK-01..07 hoàn thành (review code thật để viết docs đúng) |
| **Blocks** | — |
| **Branch** | `feat/saga/task-08-docs` |

## Goal

Tổng hợp toàn bộ kiến thức từ implementation thành tài liệu chính thức trong `docs/order/`. Đây là docs **final** (không phải task spec) — viết sau khi code thật xong để tránh drift.

## Context

Hiện tại `docs/order/` có:
- `order-service.md` — service overview (existing, update nếu cần)
- `place-sales-order.md` — sync API hiện tại (sẽ deprecate, **đừng xóa** nhưng mark deprecation)
- `saga-orchestration-plan.md` — plan tổng (TASK-08 không cần đụng)
- `tasks/` — 8 task specs (TASK-08 không cần đụng)

Task này tạo **8 file docs mới** mô tả production state sau migration.

## Files (all new)

```
docs/order/
├── README.md                              # Index toàn bộ docs Order service
├── saga-orchestration-overview.md         # Architecture + rationale
├── place-sales-order-api.md               # API contract mới (202 Accepted)
├── saga-state-machine.md                  # State diagram + transitions
├── place-sales-order-events.md            # Event catalog
├── hot-path-optimizations.md              # Redis prewarm, rate limit, fail-closed
├── place-order-normal.md                  # Sync flow + Polly config
├── operations-runbook.md                  # Trouble-shooting
└── migration-from-sync.md                 # Client migration guide
```

## Per-file specification

### 1. `README.md`

Index docs. Reading order recommendation:

1. `order-service.md` — start here (service overview)
2. `saga-orchestration-overview.md` — why saga, before/after
3. `place-sales-order-api.md` — new API contract
4. `saga-state-machine.md` — internals
5. `place-sales-order-events.md` — event reference
6. `operations-runbook.md` — when things break
7. `hot-path-optimizations.md` — flash sale specifics
8. `place-order-normal.md` — sync flow reference
9. `migration-from-sync.md` — client migration

Link mỗi file với 1-line description.

### 2. `saga-orchestration-overview.md`

- **Why**: bullet list 5 vấn đề sync orchestration (thread-pool, latency, cascading, fail-open, drift).
- **Before/after sequence diagram** (Mermaid):
  - Before: client → API → 3 sync HTTP → response (sync waterfall).
  - After: client → API → 202 → saga async → client poll → response.
- **Decision rationale**: tại sao chọn orchestration thay vì choreography, EF saga repo thay vì in-memory, etc.
- **Trade-offs**: explicit list — eventual consistency, client phải poll, debug khó hơn, etc.

### 3. `place-sales-order-api.md`

Mô tả API endpoint mới:

- `POST /api/v1/orders/sales` — request body, `202 Accepted` response, `Location` header.
- `GET /api/v1/orders/sales/{id}/status` — response DTO, polling pattern.
- Example curl flows cho:
  - Happy path: POST → poll 5x → status = Confirmed.
  - Failure path: POST → poll → status = Failed + reason.
- Error codes table (HTTP status + ErrorCode + ý nghĩa).
- Mermaid sequence diagram client polling.
- Polling guidelines: interval 500ms-2s exponential backoff, max 30s total.

### 4. `saga-state-machine.md`

- **State diagram** (Mermaid `stateDiagram-v2`):
  ```mermaid
  stateDiagram-v2
    [*] --> Initial
    Initial --> PromotionRedeeming: Requested
    PromotionRedeeming --> InventoryReserving: PromotionRedeemed
    PromotionRedeeming --> Compensating: Failed/Timeout
    InventoryReserving --> CouponClaiming: InventoryReserved + has coupon
    InventoryReserving --> Confirming: InventoryReserved no coupon
    ...
  ```
- **Transitions table**: from state, event, action, to state (full bảng từ TASK-02).
- **Timeout matrix**: state, timeout duration, fault handling.
- **Compensation flow**: sequence của events publish khi vào Compensating.
- **Edge cases**: duplicate trigger event, out-of-order responses, saga restart after crash.

### 5. `place-sales-order-events.md`

Catalog của 11 events từ TASK-01:

| Event | Source | Direction | Trigger condition | Schema link |
|---|---|---|---|---|
| `PlaceSalesOrderRequestedV1` | order-service | Order → Saga | POST /sales | [link] |
| `RedeemSalePromotionRequestedV1` | order-service | Saga → Promotion | Initial transition | ... |
| ... | | | | |

Mỗi event:
- Bảng fields (name, type, required, mô tả).
- Example JSON payload.
- Compensation event tương ứng (nếu có).
- Idempotency strategy.

### 6. `hot-path-optimizations.md`

3 optimizations từ TASK-06:

- **Pre-warm Redis quota**: cách hoạt động, key format, TTL strategy, screenshot Redis CLI inspect.
- **Idempotency fail-closed**: rationale (quota burn worse than 503), error mapping, client retry guidance.
- **Gateway rate limit**: token bucket params, partition by IP, monitoring metric `RateLimitHits`.

### 7. `place-order-normal.md`

PlaceOrder normal (sync) flow:

- Sequence diagram sync flow.
- Polly config table (per HTTP client: timeout, retry, circuit breaker).
- Atomic Coupon UsedQuota — flow + Redis-DB invariant.
- Observability — list spans, metrics, example trace screenshot.
- **Note**: docs này deprecate `place-sales-order.md` khi `place-sales-order-api.md` ready, nhưng giữ `order-service.md` overview cho cả 2 endpoints.

### 8. `operations-runbook.md`

Failure modes thường gặp:

| Symptom | Inspect command | Root cause | Action |
|---|---|---|---|
| Saga stuck ở `PromotionRedeeming` > 30s | `SELECT * FROM place_sales_order_saga_states WHERE order_id = ...` | Promotion service down | Trigger compensation manual / wait for timeout |
| 503 `SALES_ORDER_GUARD_UNAVAILABLE` spike | Aspire dashboard Redis health | Redis blip | Check Redis container, network |
| Slot drift (Redis vs DB) | Redis `GET promotion:flash:{id}:item:{key}:slots` vs `SELECT slots_reserved FROM flash_sale_items WHERE ...` | Lua/SQL desync | Run reconciliation script |
| Order pending forever | Check saga state, outbox status | Trigger event not published | Inspect `outbox_messages` table |

Metric alerts list:
- `orders.place.failure` rate > 5%/5min
- `saga.duration` p99 > 2s
- `rate_limit.hits` > 100/min

### 9. `migration-from-sync.md`

Cho client teams (frontend, mobile, BFF):

- **Breaking change**: `POST /api/v1/orders/sales` response changed `201 Created` → `202 Accepted`.
- **Required client changes**:
  - Read `Location` header hoặc body `statusUrl`.
  - Implement polling loop với exponential backoff.
  - Handle terminal states: `Confirmed`, `Failed`.
  - Map old "instant success" UX → "processing..." spinner + result.
- **Backward compatibility**: KHÔNG có. Cutoff version + deployment plan.
- **Example client code** (TypeScript fetch loop, Swift, etc. — minimal).
- **Testing**: Postman collection link, sample mock server.

## Implementation rules

1. **Viết SAU khi code thật xong** — tránh drift giữa docs và implementation.
2. **Mỗi doc đầu file có header**:
   ```markdown
   > **Last updated**: 2026-XX-XX
   > **Owner**: Order service team
   > **Status**: Production
   ```
3. **Mermaid diagrams** — verify render OK trên GitHub markdown viewer.
4. **Relative paths** cho file links — tránh hardcode `e:\Learn\...`.
5. **Code examples** copy thực từ codebase đã merge — không tự viết imaginary.
6. **Cross-link**: mỗi doc có "Related docs" section ở cuối.

## Acceptance criteria

- [ ] 9 file mới created đúng folder `docs/order/` (8 docs + README).
- [ ] Mỗi file có header metadata.
- [ ] Tất cả Mermaid diagrams render được trên GitHub.
- [ ] `place-sales-order.md` cũ được mark **DEPRECATED** ở top với link tới `place-sales-order-api.md` mới (giữ file, không delete).
- [ ] `order-service.md` được update với link tới saga-orchestration-overview.
- [ ] PR review từ 1 dev khác đã đọc qua, đảm bảo accuracy.

## Process

1. Đọc kỹ code đã merge từ TASK-01..07.
2. Tạo các file theo thứ tự: overview → API → state machine → events → operations.
3. Nhờ dev của task tương ứng review section liên quan (vd dev TASK-02 review `saga-state-machine.md`).
4. Final pass: chạy mỗi curl example, verify response thực tế match.

## Reference

- Existing service docs: [docs/order/order-service.md](../order-service.md)
- Existing sync API doc: [docs/order/place-sales-order.md](../place-sales-order.md)
- Plan tổng: [docs/order/saga-orchestration-plan.md](../saga-orchestration-plan.md)
- Markdown style guide (project): `.claude/rules/response-rules.md` mục 3 "Cập nhật docs".
