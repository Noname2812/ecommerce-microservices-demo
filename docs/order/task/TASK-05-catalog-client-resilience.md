# TASK-05 — Catalog Client + Polly Resilience

**Team:** Order · **Effort:** S (1d) · **Depends:** —
**Branch:** `feature/order-refactor/TASK-05-catalog-client`

## Mục đích

Extend `ICatalogServiceClient` để cung cấp method `GetVariantsAsync(IEnumerable<Guid> variantIds)` trả về data đủ để saga validate product/seller/price. Thêm Polly resilience (circuit breaker + retry + timeout) để chống Catalog flap.

## Files

### Modify `Order.Application/Clients/ICatalogServiceClient.cs`
Thêm method:
```csharp
public interface ICatalogServiceClient
{
    Task<Result<IReadOnlyList<CatalogVariantInfo>>> GetVariantsAsync(
        IEnumerable<Guid> variantIds,
        CancellationToken ct);

    // ... existing methods nếu có ...
}

public sealed record CatalogVariantInfo(
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    Guid VariantId,
    string Sku,
    string? VariantName,
    bool VariantIsActive,
    decimal CurrentPrice,
    Guid SellerId,
    string SellerName,
    bool SellerIsActive,
    string? ImageUrl);
```

### Modify `Order.Infrastructure/Services/CatalogServiceClient.cs`
- Implement `GetVariantsAsync`: GET `/api/v1/catalog/variants/batch?ids={id1}&ids={id2}&...`
- Deserialize response → `CatalogVariantInfo[]`
- Map HTTP errors → `Result.Failure`:
  - 404 → `OrderErrors.CatalogValidationFailed("VARIANT_NOT_FOUND")`
  - 503/timeout/CB open → `OrderErrors.CatalogUnavailable`
  - 5xx khác → `OrderErrors.CatalogUnavailable`

### Catalog side — verify endpoint exists

**`src/Services/Catalog/UrbanX.Catalog.API/Apis/...`** — verify route:
```
GET /api/v1/catalog/variants/batch?ids={guid}&ids={guid}
```
Nếu chưa có:
- Tạo query `GetVariantsBatchInternalQuery(IReadOnlyList<Guid> ids)` trong `Catalog.Application/Usecases/V1/Query/`
- Mark `[AllowAnonymous]` (internal service-to-service call, không qua Gateway)
- Tạo endpoint trong `ProductApis.cs` hoặc `VariantApis.cs`
- Return list `CatalogVariantInfoDto` (mirror struct trong Order)

⚠ Coordinate với Catalog team — đây là cross-team change. Nếu Catalog team busy, fallback: dùng existing `GET /api/v1/catalog/variants/{id}` gọi song song nhiều lần (parallel HTTP).

### Modify `Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`
Thêm Polly Standard Resilience:
```csharp
services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>((sp, client) =>
{
    // BaseAddress, headers, etc.
})
.AddStandardResilienceHandler(o =>
{
    // Circuit breaker
    o.CircuitBreaker.SamplingDuration   = TimeSpan.FromSeconds(30);
    o.CircuitBreaker.FailureRatio       = 0.5;
    o.CircuitBreaker.MinimumThroughput  = 10;
    o.CircuitBreaker.BreakDuration      = TimeSpan.FromSeconds(10);

    // Retry
    o.Retry.MaxRetryAttempts            = 2;
    o.Retry.UseJitter                    = true;
    o.Retry.BackoffType                  = DelayBackoffType.Exponential;

    // Timeouts
    o.AttemptTimeout.Timeout             = TimeSpan.FromSeconds(3);
    o.TotalRequestTimeout.Timeout        = TimeSpan.FromSeconds(10);
});
```

### Update `Directory.Packages.props`
Thêm:
```xml
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />
```

### Update `Order.Infrastructure.csproj`
Thêm:
```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" />
```

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit test: mock HttpClient, verify `GetVariantsAsync` parse response đúng
- [ ] Integration test với Catalog service real:
  - Happy: 5 variantIds → 5 CatalogVariantInfo
  - 1 variantId không tồn tại → `Result.Failure("VARIANT_NOT_FOUND")`
  - Catalog down → after 5 retries CB open → `Result.Failure("CATALOG_UNAVAILABLE")`
- [ ] Circuit breaker test: 10 fail liên tiếp → CB open, request thứ 11 fail-fast (không gọi HTTP)
- [ ] Telemetry: verify OTLP traces có span `HttpClient.SendAsync` với CB status

## Notes

- `AddStandardResilienceHandler` của Microsoft.Extensions.Http.Resilience cung cấp Polly v8 chuẩn (chain: rate limiter → total timeout → retry → CB → attempt timeout)
- Default sufficient; chỉ override cần thiết
- KHÔNG cấu hình CB ở consumer-level — đặt tại HttpClient registration

## DoD

- [ ] Tests pass
- [ ] PR merge
- [ ] Unblock TASK-07, 08
