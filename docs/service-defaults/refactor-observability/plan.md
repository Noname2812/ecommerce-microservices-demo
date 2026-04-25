# Plan: Refactor ServiceDefaults + Grafana/Prometheus Observability

## Mục tiêu
Tách `Extensions.cs` thành các files theo từng responsibility. Thay thế exporter hiện tại bằng Prometheus scrape endpoint cho metrics và OTLP → Grafana Tempo cho tracing.

## Hướng đã chọn
**Hướng A — Prometheus + Grafana Tempo:** Grafana-native stack, metrics và traces xem trong cùng 1 UI Grafana.

---

## Các bước thực hiện

### 1. Tách Extensions.cs thành 4 files riêng
`Extensions.cs` chỉ còn `AddServiceDefaults` orchestrator. 4 files mới:

| File | Responsibility |
|---|---|
| `Extensions/ObservabilityExtensions.cs` | OTel: logging + metrics (Prometheus) + tracing (OTLP→Tempo) |
| `Extensions/HealthCheckExtensions.cs` | `AddDefaultHealthChecks` |
| `Extensions/HttpClientExtensions.cs` | Resilience + service discovery |
| `Extensions/MiddlewareExtensions.cs` | `MapDefaultEndpoints`, `UseProductionDefaults`, `MapErrorEndpoint`, `MapPrometheusScrapingEndpoint` |

(không có skill riêng — follow pattern hiện có)

---

### 2. Thêm Prometheus exporter package
- `Directory.Packages.props`: thêm `OpenTelemetry.Exporter.Prometheus.AspNetCore`
- `UrbanX.ServiceDefaults.csproj`: thêm `<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" />`

(không có skill riêng)

---

### 3. Cập nhật ObservabilityExtensions — metrics + tracing exporters

Metrics mới → Prometheus scrape (pull model):
```csharp
metrics.AddPrometheusExporter(); // thêm vào WithMetrics()
```

Tracing → OTLP vẫn giữ, inject endpoint từ AppHost trỏ đến Tempo:
```csharp
// OTEL_EXPORTER_OTLP_ENDPOINT inject từ AppHost → Tempo port 4317
builder.Services.AddOpenTelemetry().UseOtlpExporter();
```

Xóa `AddOpenTelemetryExporters` (private method export OTLP cho metrics).

(không có skill riêng)

---

### 4. Cập nhật MiddlewareExtensions — expose `/metrics` endpoint
```csharp
app.MapPrometheusScrapingEndpoint(); // thêm vào MapDefaultEndpoints
```

(không có skill riêng)

---

### 5. AppHost — thêm Grafana Observability Stack
Thêm 3 containers vào `AppHost.cs`:
- **Grafana Tempo** — nhận OTLP traces từ services (port 4317 gRPC, 3200 HTTP)
- **Prometheus** — scrape `/metrics` từ từng service (port 9090)
- **Grafana** — visualize (port 3000), auto-provision Prometheus + Tempo datasources

Inject `OTEL_EXPORTER_OTLP_ENDPOINT` vào catalog, search, gateway → Tempo endpoint.

Dùng fixed ports cho services (`.WithHttpEndpoint(port: XXXX)`) để Prometheus scrape config tĩnh được.

(không có skill riêng)

---

### 6. Tạo config files cho Prometheus và Tempo

| File | Mục đích |
|---|---|
| `src/AppHost/UrbanX.AppHost/config/prometheus.yml` | Scrape targets cho catalog, search, gateway |
| `src/AppHost/UrbanX.AppHost/config/tempo.yaml` | Tempo storage + OTLP receiver config |
| `src/AppHost/UrbanX.AppHost/config/grafana/provisioning/datasources/datasources.yaml` | Auto-provision Prometheus + Tempo datasources |

---

## Files cần tạo mới
- `src/ServiceDefaults/UrbanX.ServiceDefaults/Extensions/ObservabilityExtensions.cs`
- `src/ServiceDefaults/UrbanX.ServiceDefaults/Extensions/HealthCheckExtensions.cs`
- `src/ServiceDefaults/UrbanX.ServiceDefaults/Extensions/HttpClientExtensions.cs`
- `src/ServiceDefaults/UrbanX.ServiceDefaults/Extensions/MiddlewareExtensions.cs`
- `src/AppHost/UrbanX.AppHost/config/prometheus.yml`
- `src/AppHost/UrbanX.AppHost/config/tempo.yaml`
- `src/AppHost/UrbanX.AppHost/config/grafana/provisioning/datasources/datasources.yaml`

## Files cần chỉnh sửa
- `src/ServiceDefaults/UrbanX.ServiceDefaults/Extensions.cs` — chỉ giữ `AddServiceDefaults` orchestrator
- `src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj` — thêm Prometheus exporter package
- `Directory.Packages.props` — thêm `OpenTelemetry.Exporter.Prometheus.AspNetCore`
- `src/AppHost/UrbanX.AppHost/AppHost.cs` — thêm Tempo + Prometheus + Grafana containers

## Migration
- Không cần migration

## Integration events
- Không có

## Rủi ro / Lưu ý

1. **Prometheus scrape target — dynamic ports:** Aspire services chạy trên host với dynamic ports, Prometheus container cần scrape `host.docker.internal:<port>`. Cần dùng fixed port (`.WithHttpEndpoint(port: XXXX)`) cho mỗi service trong AppHost để prometheus.yml có scrape target tĩnh.

2. **OTLP exporter scope:** `UseOtlpExporter()` hiện export cả metrics + traces. Sau refactor, metrics đi Prometheus (pull), traces đi OTLP (push → Tempo). Cần dùng `AddOtlpExporter()` chỉ trong `WithTracing()`, không dùng global `UseOtlpExporter()`.

3. **Tempo storage:** Tempo cần local filesystem — bind mount `./tempo-data` hoặc dùng in-memory mode cho dev.

4. **`Microsoft.AspNetCore.Identity.EntityFrameworkCore` trong ServiceDefaults.csproj:** Package không liên quan đến ServiceDefaults, nên xóa nếu không có service nào depend vào nó qua project này.

## Docs cần cập nhật
- `docs/service-defaults/refactor-observability.md`
