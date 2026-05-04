# Place Order — Task Dependency & Parallel Execution Strategy

**Sprint:** Place Order v1.0  
**Team:** 1 Senior (A) · 1 Mid-B (Inventory) · 1 Mid-C (Coupon) · 1 Junior (D)  
**Total:** ~15 ngày

---

## Nguyên tắc đọc bảng

| Ký hiệu | Ý nghĩa |
|---|---|
| 🔴 **Blocking** | Phải xong hoàn toàn trước khi task tiếp theo bắt đầu |
| 🟣 **Tuần tự** | Phụ thuộc output của task trước trong cùng luồng |
| 🟢 **Song song** | Có thể chạy đồng thời với task khác |
| ⏸ **Chờ** | Chưa bắt đầu được, đang đợi dependency từ luồng khác |

---

## Timeline tổng quan

```
Ngày:   1        2        3        4        5        6        7        8        9       10       11       12       13       14       15
        ├────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┤

[A]  ██P1-T1██████P1-T2█        ⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸        ██P4-T4██P4-T5██        ████████P4-T6████████
[D]           ██P1-T3██ ██P1-T4██ █P1-T5█  ⏸⏸⏸⏸⏸⏸⏸⏸⏸⏸       █P4-T1█  ████P4-T2████  ██P4-T3██  ██P5-T1██P5-T3██  ████P5-T2+T4████

[B]           ██P2-T1██  ██P2-T5██  ████████P2-T2████████  ██P2-T3██  ██P2-T4██
[C]           ██P3-T1██  ██P3-T5██  ████████P3-T2████████  ██P3-T3██  ██P3-T4██

[All]                                                                            ████████████P6-T1+T2████████████  ██P6-T3██
```

---

## Critical Path

```
P1-T1 → P1-T2 → P2-T5 TTL (B) → P2-T2 Reserve API (B)
                P3-T5 TTL (C) → P3-T2 Claim API (C)
                                                    → P4-T4 + P4-T5 → P4-T6 → P6-T3
```

> Bất kỳ task nào trên critical path trễ → **toàn bộ timeline dịch theo**.  
> Ưu tiên unblock critical path trước mọi task khác.

---

## P1 — Foundation (Ngày 1–2)

### Thứ tự trong P1

```
Ngày 1 sáng:
  [A] P1-T1 Broker setup  ← 🔴 BLOCKING — làm đầu tiên, không có cái này không ai làm được

Ngày 1 chiều (sau P1-T1 xong):
  [A] P1-T2 Contracts     ← 🟣 Tuần tự sau T1
  [D] P1-T3 Outbox infra  ← 🟢 Song song với T2 (D và A làm cùng lúc)
  [D] P1-T4 Idempotency   ← 🟢 Song song với T3 (không depend nhau)

Ngày 2:
  [D] P1-T5 CompOutbox    ← 🟣 Sau T3 (dùng chung pattern Outbox)
  [B] bắt đầu P2-T1       ← 🟢 Ngay khi T1+T2 xong
  [C] bắt đầu P3-T1       ← 🟢 Ngay khi T1+T2 xong
```

### Chi tiết dependency P1

| Task | Depends on | Lý do |
|---|---|---|
| P1-T1 Broker | — | Task đầu tiên tuyệt đối |
| P1-T2 Contracts | P1-T1 | Cần biết broker topology để define routing |
| P1-T3 Outbox | P1-T1 | Cần MassTransit config từ T1 |
| P1-T4 Idempotency | P1-T1 | Cần Redis config từ T1 |
| P1-T5 CompOutbox | P1-T3 | Dùng chung base pattern của T3 |

### Song song trong P1

- ✅ P1-T2 và P1-T3 **song song được** — A và D làm cùng lúc từ chiều ngày 1
- ✅ P1-T3 và P1-T4 **song song được** — D có thể chia đôi làm cùng ngày
- ❌ P1-T5 **không song song với T3** — T5 cần T3 xong trước để copy pattern

---

## P2 — Inventory Service (Ngày 2–5)

### Thứ tự trong P2

```
Ngày 2 (sau P1-T2 xong):
  [B] P2-T1 Schema        ← 🟣 Bắt đầu ngay, không cần đợi gì thêm

Ngày 2–3:
  [B] P2-T5 TTL Job       ← 🔴 BLOCKING — deploy và verify chạy ổn
                             TRƯỚC KHI Reserve API live

Ngày 3–4:
  [B] P2-T2 Reserve API   ← 🟣 Sau T1 + T5 đã deploy

Ngày 4:
  [B] P2-T3 Release API   ← 🟣 Sau T2

Ngày 4–5:
  [B] P2-T4 Consumer      ← 🟣 Sau T3 (dùng release logic)
```

### Chi tiết dependency P2

| Task | Depends on | Lý do |
|---|---|---|
| P2-T1 Schema | P1-T1, P1-T2 | Cần contracts để define foreign keys |
| P2-T5 TTL Job | P2-T1 | Cần schema tồn tại |
| P2-T2 Reserve API | P2-T1, **P2-T5 deployed** | TTL phải chạy trước khi API mở |
| P2-T3 Release API | P2-T2 | Release là nghịch của Reserve |
| P2-T4 Consumer | P2-T3, P1-T2 | Dùng Release logic + cần IInventoryReleaseRequested contract |

### Song song trong P2

- ✅ P2-T3 và P2-T4 **có thể overlap** — T4 bắt đầu khi T3 gần xong
- ❌ Không có task nào trong P2 song song được với nhau — luồng tuyến tính

> **⚠️ Quy tắc TTL-first:** P2-T5 phải được deploy lên môi trường test và verify job chạy thành công ÍT NHẤT 1 lần trước khi P2-T2 Reserve API được merge vào main.

---

## P3 — Coupon Service (Ngày 2–5)

### Thứ tự trong P3

```
Ngày 2 (sau P1-T2 xong):
  [C] P3-T1 Schema + Redis ← 🟢 Song song với P2-T1 (B và C làm cùng lúc)

Ngày 2–3:
  [C] P3-T5 TTL Job        ← 🔴 BLOCKING — tương tự P2-T5

Ngày 3–4:
  [C] P3-T2 Claim API      ← 🟣 Sau T1 + T5

Ngày 4:
  [C] P3-T3 Release API    ← 🟣 Sau T2

Ngày 4–5:
  [C] P3-T4 Consumer       ← 🟣 Sau T3
```

### Song song P2 vs P3

```
B: P2-T1 ──── P2-T5 ──── P2-T2 ──── P2-T3 ──── P2-T4
C: P3-T1 ──── P3-T5 ──── P3-T2 ──── P3-T3 ──── P3-T4
   ↑                   ↑
   Cùng bắt đầu ngày 2  Cùng bắt đầu ngày 3
```

- ✅ **P2 và P3 hoàn toàn độc lập** — B và C không block nhau ở bất kỳ bước nào
- ✅ Mỗi task tương ứng (P2-T1 và P3-T1, P2-T2 và P3-T2, ...) chạy song song theo thời gian

---

## P4 — Order Service (Ngày 6–9)

### Điều kiện bắt đầu P4

> P4 chỉ bắt đầu khi **P2 và P3 đều có API chạy được** (Reserve + Claim endpoint trả đúng response).  
> Không cần đợi Consumer và TTL job của P2/P3 hoàn thiện.

### Thứ tự trong P4

```
Ngày 6 — A và D làm song song:
  [A] P4-T4 Inventory client    ← 🟢 Song song với T5 và với D
  [A] P4-T5 Coupon client       ← 🟢 Song song với T4
  [D] P4-T1 Schema              ← 🟢 Song song với A
  [D] P4-T2 Validation pipeline ← 🟢 Song song với T1 (chỉ cần FluentValidation)

Ngày 7 — merge outputs:
  [D] P4-T3 Pricing Service     ← 🟣 Sau T2 (cần validation xong)
  [A] P4-T6 Core handler        ← 🟣 Sau T4 + T5 + D's T1 + T2 + T3

Ngày 7–8:
  [A] P4-T6 Core handler        ← Task quan trọng nhất, A tập trung
```

### Chi tiết dependency P4

| Task | Depends on | Lý do |
|---|---|---|
| P4-T1 Schema | P1-T3, P1-T5 | Cần Outbox + CompOutbox tables |
| P4-T2 Validation | P1-T2 | Cần Contracts để validate Items |
| P4-T3 Pricing | P4-T2 | Pricing cần input đã validate |
| P4-T4 Inv client | P2-T2 (API live) | Không thể implement client khi chưa có API |
| P4-T5 Cpn client | P3-T2 (API live) | Tương tự T4 |
| P4-T6 Core handler | **T4+T5+T1+T2+T3** | Orchestrate tất cả — task cuối P4 |

### Song song trong P4

- ✅ P4-T4 và P4-T5 **song song** (A làm cả 2 trong buổi sáng ngày 6)
- ✅ P4-T1, P4-T2 **song song với T4+T5** (D và A làm cùng lúc)
- ✅ P4-T1 và P4-T2 **song song với nhau** (D chia đôi)
- ❌ P4-T6 **không thể bắt đầu** cho đến khi T1+T2+T3+T4+T5 đều xong

---

## P5 — Resilience (Ngày 10–11)

### Thứ tự trong P5

```
Ngày 10:
  [D] P5-T1 Polly + Circuit Breaker ← 🟢 Song song với T3
  [D] P5-T3 Distributed Tracing     ← 🟢 Song song với T1

Ngày 10–11:
  [D] P5-T2 Saga watchdog           ← 🟣 Sau T1+T3 setup xong
  [D] P5-T4 Logging + Metrics       ← 🟣 Song song với T2 (merge cuối ngày 11)
```

### Chi tiết dependency P5

| Task | Depends on | Lý do |
|---|---|---|
| P5-T1 Polly | P4-T4, P4-T5 | Wrap HttpClient đã có |
| P5-T3 Tracing | P4 hoàn thành | Cần có services để instrument |
| P5-T2 Watchdog | P4-T6 | Cần biết saga states |
| P5-T4 Logging | P4 hoàn thành | Metrics meaningful khi có full flow |

- ✅ P5-T1 và P5-T3 **song song** — D làm cả 2 trong ngày 10

---

## P6 — Testing (Ngày 11–15)

### Thứ tự trong P6

```
Ngày 11–13:
  [A+D] P6-T1 Integration tests ← 🟢 Song song với T2
  [B+C] P6-T2 Load tests        ← 🟢 Song song với T1
                                    B test P2, C test P3 — độc lập nhau

Ngày 13–14:
  [All] P6-T3 E2E smoke test    ← 🟣 Sau T1 + T2 pass hết
```

### Song song trong P6

- ✅ P6-T1 và P6-T2 **song song** — chia theo service ownership
- ✅ B và C **song song** trong P6-T2 (mỗi người test service mình
- ❌ P6-T3 phải đợi T1 + T2 **không có failing test** mới bắt đầu

---

## Tổng hợp: Những điểm song song quan trọng nhất

| Cặp song song | Từ ngày | Tiết kiệm |
|---|---|---|
| P1-T2 (A) + P1-T3 (D) | Ngày 1 chiều | ~4h |
| P2 (B) toàn bộ + P3 (C) toàn bộ | Ngày 2–5 | ~3 ngày |
| P4-T4+T5 (A) + P4-T1+T2 (D) | Ngày 6 | ~1 ngày |
| P5-T1 + P5-T3 (D) | Ngày 10 | ~4h |
| P6-T1 (A+D) + P6-T2 (B+C) | Ngày 11 | ~1 ngày |

> **Tổng tiết kiệm so với làm tuần tự:** ~5–6 ngày  
> Nếu làm hoàn toàn tuần tự: ~20–22 ngày → Song song rút còn: **~15 ngày**

---

## Những điểm tuần tự bắt buộc — không thể song song hóa

1. **P1-T1 phải là task số 1** — không có broker thì không ai làm được gì
2. **P1-T2 (Contracts) phải trước P2/P3** — B và C cần interface definitions
3. **TTL jobs (P2-T5, P3-T5) phải deploy trước Reserve/Claim API** — safety net phải có trước
4. **P4 phải sau P2 và P3** — không thể viết Order Service khi chưa có API để gọi
5. **P4-T6 là task cuối P4** — core handler cần tất cả pieces từ T1→T5
6. **P6-T3 E2E là task cuối cùng** — chỉ chạy khi mọi integration + load test pass

---

## Checklist sync hàng ngày

Mỗi buổi sáng kiểm tra:

- [ ] Task đang chạy có bị block bởi dependency chưa xong không?
- [ ] TTL jobs đã verify chạy chưa? (trước khi API go live)
- [ ] Contracts NuGet package có version mới không? (cập nhật B và C)
- [ ] PR review backlog — không để quá 24h không review