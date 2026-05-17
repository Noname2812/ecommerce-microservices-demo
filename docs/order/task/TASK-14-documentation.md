# TASK-14 — Documentation Update

**Team:** Order · **Effort:** S (0.5d) · **Depends:** All previous tasks
**Branch:** `feature/order-refactor/TASK-14-docs`

## Mục đích

Hoàn thiện docs cho Order service refactor:
- Cập nhật `async-ticket-flow.md` (đã có draft, finalize sau implementation)
- Tạo `reserve-vs-deduct.md` giải thích semantic Reserve → Deduct
- Cập nhật CLAUDE.md project root
- Cleanup old docs nếu cần

## Files MODIFY

### `docs/order/async-ticket-flow.md`
- Đã có draft từ initial design ([link](../async-ticket-flow.md))
- Sau khi implementation xong, cập nhật:
  - Status: "Approved" → "Implemented YYYY-MM-DD"
  - Bổ sung deviation / lesson learned nếu có
  - Verify diagrams accurate so với code
  - Update links đến file source code thực tế

### `CLAUDE.md` (project root)
- Update Service Map nếu Order service thêm capabilities:
  - "Order: ... Async ticket flow + saga orchestration"
- Update "Key Patterns" section thêm note:
  - "Async ticket flow: handler trả 202, saga đảm nhận validate/save/reserve/payment (xem `docs/order/async-ticket-flow.md`)"
  - "MT EF Outbox dùng cho Order service; các service khác giữ Shared.Outbox"

### `.claude/CLAUDE.md` (project local)
- Update tương tự nếu có references

## Files NEW

### `docs/order/reserve-vs-deduct.md`
```markdown
# Inventory: Reserve vs Deduct Semantic

## Khái niệm

### Reserve (Soft Lock)
- Khi Order ở PENDING_PAYMENT (chờ payment): saga publish `ReserveInventoryRequestedV1`
- Inventory tạo `InventoryReservation` với Status=Pending, ExpiresAt=now+15min
- `InventoryItem.QuantityReserved += qty` (tăng reserved)
- `InventoryItem.QuantityOnHand` KHÔNG đổi (stock thật chưa giảm)
- `QuantityAvailable = QuantityOnHand - QuantityReserved` (computed) — phản ánh stock có thể bán

### Deduct (Hard Deduct)
- Khi Payment thành công, Order → CONFIRMED: saga publish `ConfirmInventoryRequestedV1`
- Inventory chuyển `InventoryReservation.Status` Pending → Confirmed
- `InventoryItem.QuantityReserved -= qty` (giảm reserved)
- `InventoryItem.QuantityOnHand -= qty` (giảm stock thật — hard deduct)
- Tạo `StockMovement` audit trail (MovementType=OUTBOUND, Reason="ORDER_CONFIRMED")

### Release
- Khi timeout / cancel: saga publish `InventoryReleaseRequestedV1`
- `InventoryReservation.Status` Pending → Released
- `InventoryItem.QuantityReserved -= qty` (giảm reserved)
- `QuantityOnHand` KHÔNG đổi (chưa từng giảm)

## Tại sao tách Reserve vs Deduct?

| Reason | Detail |
|---|---|
| **Soft lock cho UX** | User có 15 phút thanh toán, không muốn người khác cướp stock; nhưng cũng không giảm `QuantityOnHand` vì chưa thực sự bán |
| **Track stock thực bán** | `QuantityOnHand` phản ánh stock kho thực; sau Deduct mới giảm — analytics, reporting chính xác |
| **Rollback dễ** | Timeout/cancel chỉ release reservation, không cần "đắp lại" stock |
| **Audit** | Stock movement chỉ record khi Deduct, không noise từ Reserve/Release |

## State machine

```
Reservation Status: Pending → Confirmed     (sau ConfirmReservation, payment OK)
                          ↘ Released        (sau ReleaseReservation, cancel/timeout)
```

```
InventoryItem fields:
  QuantityOnHand   = stock vật lý kho
  QuantityReserved = đang soft-lock cho PENDING_PAYMENT orders
  QuantityAvailable = OnHand - Reserved (computed; có thể bán cho order mới)
```

## Race conditions

| Scenario | Handle |
|---|---|
| ConfirmReservation + Release race | ConfirmReservation idempotent guard `Status == Confirmed → skip` |
| Concurrent Reserve cho cùng variant | PostgreSQL `xmin` shadow property + `IConcurrencyRetriableCommand` retry |
| ConfirmReservation 2 lần (MT redelivery) | Handler check `Status == Confirmed` → return Success ngay, không double-deduct |

## Code references

- `Inventory.Domain/Models/InventoryReservation.cs` — `Confirm()`, `MarkReleased()`
- `Inventory.Domain/Models/InventoryItem.cs` — `ConfirmDeduction()`, `ReleaseReservedQuantity()`
- `Inventory.Application/Usecases/V1/Command/ReserveInventory/` — soft lock
- `Inventory.Application/Usecases/V1/Command/ConfirmReservation/` — hard deduct (TASK-09)
- `Inventory.Application/Usecases/V1/Command/ReleaseInventory/` — rollback

## See also

- [`async-ticket-flow.md`](async-ticket-flow.md) — flow diagram saga gọi reserve/confirm/release
```

### ADR (optional)

Nếu team dùng ADR: tạo `docs/adr/0001-async-ticket-flow.md` ghi nhận:
- Context (why decoupling handler từ saga)
- Decision (saga tạo Order, MT EF Outbox, etc.)
- Consequences (UX polling thay vì synchronous return, infra complexity)
- Alternatives considered (sync return + outbox; SSE; durable execution)

## Cleanup old docs

Verify và clean:
- `docs/order/place-order-async.md` — nếu trùng nội dung với `async-ticket-flow.md`, merge hoặc xoá
- `docs/order/tasks/TASK-*.md` cũ — kiểm tra git log, nếu là planning artifact từ trước, archive/delete

## Acceptance Criteria

- [ ] `async-ticket-flow.md` updated với status "Implemented" + actual code paths
- [ ] `reserve-vs-deduct.md` tạo mới
- [ ] CLAUDE.md (root) updated
- [ ] Old/duplicate docs cleaned hoặc archived
- [ ] All doc links work (verify `[text](path)` paths)
- [ ] PR review approve

## DoD

- [ ] PR merge
- [ ] Announce trong team channel: refactor complete, ref docs
