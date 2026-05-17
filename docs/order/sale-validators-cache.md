# Sale validators — tiered cache lookup

`SaleEligibilityValidator` và `SalePricingValidator` không gọi HTTP sang Promotion service nữa. Toàn bộ kiểm tra trên thời gian thực dựa vào dữ liệu cache mà Promotion warm up sẵn trước khi sale bắt đầu.

## Tier layout

```
L1: in-process IMemoryCache  (TTL ngắn ~10s, làm mịn traffic Redis)
L2: Redis                    (Promotion cronjob ghi trước khi campaign mở)
(no HTTP fallback — cache miss = campaign không khả dụng)
```

## Cache keys (cùng namespace `sale:*` với `SaleAllocationGate`)

| Key | Value type | Producer | Consumer |
|---|---|---|---|
| `sale:{campaignId}:meta` | `CampaignSnapshot` (`StartsAt`, `EndsAt`, `IsActive`) | Promotion warm-up cronjob | `SaleEligibilityValidator` |
| `sale:{campaignId}:prices` | `Dictionary<Guid, decimal>` (variantId → sale price) | Promotion warm-up cronjob | `SalePricingValidator` |
| `sale:{campaignId}:quota` | `long` (global quota counter) | Promotion warm-up cronjob | `SaleAllocationGate.TryReserveAsync` |
| `sale:{campaignId}:user:{userId}` | `int` (per-user redeemed count) | `SaleAllocationGate` (atomic Lua) | `SaleAllocationGate` |

## Validator behavior

**`SaleEligibilityValidator`**
- Đọc `sale:{campaignId}:meta` → check `IsActive` + thời gian nằm trong `[StartsAt, EndsAt]`.
- Cache miss / inactive / out-of-window → `OrderErrors.SaleCampaignInvalid(...)`.
- Quota per-user/global do `ISaleAllocationGate.TryReserveAsync` enforce nguyên tử ở bước tiếp theo trong handler.

**`SalePricingValidator`**
- Đọc `sale:{campaignId}:prices` → đối chiếu từng `item.UnitPrice` với giá kỳ vọng (tolerance `0.01`).
- Empty / missing variant → `OrderErrors.SalePricingUnavailable`.
- Price chênh > tolerance → `OrderErrors.PriceMismatch(sku, expected, actual)`.

## Parallel validation

Trong `PlaceSalesOrderCommandHandler` cả 4 validator chạy song song qua `ParallelValidator.RunAsync`:
- `eligibilityValidator` + `productValidator` + `shippingValidator` + `salePricingValidator`
- Tất cả là read-only, không có side-effect.
- Chỉ khi 4 validator pass mới reserve quota (`SaleAllocationGate.TryReserveAsync`) → không lãng phí slot khi validation fail.

## Configuration

```jsonc
// appsettings.json
"Order": {
  "SaleSnapshot": {
    "MemoryCacheTtlSeconds": 10  // TTL của L1 IMemoryCache
  }
}
```

## DI

```csharp
// UrbanX.Order.Infrastructure
services.AddOptions<SaleSnapshotOptions>()
    .BindConfiguration(SaleSnapshotOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

services.AddMemoryCache();
services.AddScoped<ISaleSnapshotCache, MemoryRedisSaleSnapshotCache>();
```

## Telemetry

`MemoryRedisSaleSnapshotCache` ghi cùng meter với catalog (`UrbanX.Order`):
- Counter `order.validator.source` với tag `validator = sale_eligibility | sale_pricing`, `source = memory_hit | redis_hit | miss`.
- Histogram `order.validator.duration_ms`.

## Trách nhiệm warm-up (Promotion side)

Trước khi campaign mở:
1. Cronjob đọc dữ liệu campaign từ DB.
2. Ghi `sale:{campaignId}:meta` (TTL = `EndsAt + buffer`).
3. Ghi `sale:{campaignId}:prices` (cùng TTL).
4. Khởi tạo `sale:{campaignId}:quota` = tổng quota.

Nếu cronjob chưa chạy → Order trả về `SaleCampaignInvalid` (đúng UX cho sale chưa mở).
