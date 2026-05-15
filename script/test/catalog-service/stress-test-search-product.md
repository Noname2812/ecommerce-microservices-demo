# Stress test — Catalog search product

Tài liệu này mô tả cách đo **throughput (req/s)** và **độ trễ** của chức năng tìm kiếm sản phẩm trên Catalog service.

## Endpoint thực tế

Search không phải route riêng: khi gọi **GET** danh sách sản phẩm có tham số `q`, server chạy full-text search (`SearchProductsQuery`).

- **Path:** `/api/v1/catalog/products`
- **Query:** `q` (bắt buộc cho search), tùy chọn `categoryId`, `priceMin`, `priceMax`, `sort`, `page`, `pageSize`
- **Auth:** `[AllowAnonymous]` — không cần JWT khi gọi thẳng Catalog API.

Ví dụ URL đầy đủ:

```http
GET http://localhost:5025/api/v1/catalog/products?q=Seed&page=1&pageSize=20&sort=relevance
```

Dữ liệu seed mặc định có tên dạng `Seed Product 1` … `Seed Product 10`, nên `q=Seed` hoặc `q=Product` thường có kết quả.

## Gọi thẳng Catalog hay qua Gateway?

| Cách gọi | URL gốc (mặc định dev) | Lưu ý khi stress test |
|----------|-------------------------|------------------------|
| **Catalog trực tiếp** | `http://localhost:5025` (`launchSettings` của `UrbanX.Catalog.API`) | Đo được **khả năng xử lý của Catalog + PostgreSQL** ít bị che bởi proxy. |
| **Qua Gateway** | `http://localhost:5000` (`UrbanX.Gateway`) | Gateway có **rate limiting** theo IP (ví dụ anonymous GET rơi vào bucket `global:{ip}` — mặc định khoảng **1000 request / 60 giây** mỗi IP, xem `appsettings.json` → `RateLimit`). Rất dễ thấy **429** khi bắn cao, đó **không** phản ánh đúng RPS tối đa của Catalog. |

**Khuyến nghị:** stress test **capacity** của search → bắn vào **Catalog** (`BASE_URL=http://localhost:5025`). Chỉ test qua Gateway khi bạn muốn mô phỏng **người dùng thật + giới hạn edge**.

Nếu chạy bằng **Aspire** (`dotnet run` trong `UrbanX.AppHost`), cổng có thể khác — lấy URL Catalog từ dashboard Aspire hoặc log khi service khởi động.

## Chuẩn bị

1. PostgreSQL + migration Catalog (thường tự chạy khi start API).
2. Chạy Catalog API (hoặc full stack Aspire).
3. Kiểm tra nhanh bằng trình duyệt/curl:

```powershell
Invoke-WebRequest "http://localhost:5025/api/v1/catalog/products?q=Seed&page=1&pageSize=20"
```

## Cách 1 — k6 (nên dùng)

Cài k6: [https://k6.io/docs/get-started/installation/](https://k6.io/docs/get-started/installation/)

Trong thư mục `script/test/catalog-service/`:

### 1a. Ramping VUs (tăng dần số user ảo — xem hệ thống “gãy” ở mức nào)

```powershell
cd d:\learn\urbanx-sample\script\test\catalog-service
k6 run stress-test-search-product.k6.js
```

Mặc định: `BASE_URL=http://localhost:5025`, `SEARCH_Q=Seed`, `EXECUTOR=vus`.

### 1b. Cố định RPS (đo xem **mục tiêu X req/s** có giữ được không)

Tăng dần `TARGET_RPS` (50 → 100 → 200 …) cho đến khi `http_req_failed` tăng hoặc latency vượt ngưỡng chấp nhận:

```powershell
k6 run -e EXECUTOR=rps -e TARGET_RPS=100 -e DURATION=2m -e PRE_VUS=100 -e MAX_VUS=400 stress-test-search-product.k6.js
```

Biến môi trường hữu ích:

| Biến | Ý nghĩa | Mặc định |
|------|---------|----------|
| `BASE_URL` | Gốc HTTP của Catalog | `http://localhost:5025` |
| `SEARCH_Q` | Từ khóa `q` | `Seed` |
| `EXECUTOR` | `vus` (ramping) hoặc `rps` (constant-arrival-rate) | `vus` |
| `TARGET_RPS` | (chỉ `rps`) số request mong muốn mỗi giây | `50` |
| `DURATION` | (chỉ `rps`) thời lượng scenario | `2m` |
| `PRE_VUS` / `MAX_VUS` | (chỉ `rps`) VU dự trữ / tối đa | `80` / `400` |

### Đọc kết quả k6 (tóm tắt)

- **`http_reqs`** và **`iteration_duration`** — throughput thực tế.
- **`http_req_duration` p(95)** — độ trễ người dùng cảm nhận.
- **`http_req_failed`** — tỷ lệ lỗi (timeout, 5xx, check fail).
- **`checks`** — tỷ lệ response HTTP 200.

“Chịu được bao nhiêu req/s” thường là **RPS cao nhất** mà vẫn giữ `http_req_failed ≈ 0` và `p(95)` trong ngưỡng bạn chấp nhận (ví dụ dưới 500 ms hoặc dưới 1 giây).

## Cách 2 — PowerShell (không cần k6)

Yêu cầu **PowerShell 7+**.

```powershell
cd d:\learn\urbanx-sample\script\test\catalog-service
.\stress-test-search-product.ps1 -BaseUrl http://localhost:5025 -Query Seed -Concurrency 50 -TotalRequests 5000
```

Script in ra **req/s trung bình** (tổng request / thời gian wall-clock). Độ chính xác latency/phân vị thấp hơn k6; phù hợp smoke test nhanh.

## File trong thư mục

| File | Mục đích |
|------|----------|
| `stress-test-search-product.md` | Hướng dẫn (file này) |
| `stress-test-search-product.k6.js` | Kịch bản k6 |
| `stress-test-search-product.ps1` | Script PowerShell đơn giản |

## Gợi ý thêm (tùy chọn)

- Chạy k6 từ **máy khác** trong mạng LAN để tránh CPU client làm nghẽn.
- Theo dõi **PostgreSQL** (CPU, `pg_stat_statements`, connection pool) và **Catalog** (GC, thread pool) khi RPS cao.
- So sánh cùng workload với/s không có **Redis** (nếu sau này search có cache) để biết bottleneck.


docker run --rm williamyeh/wrk -t8 -c200 -d60s http://host.docker.internal:5025/api/v1/catalog/products?q=seed


App đã có sẵn instrumentation — nhìn vào Aspire dashboard:

cache.l2.get span duration: nếu > 200ms → Redis là bottleneck
cache.lock.acquire span duration: nếu > 100ms → lock/Redis slow
catalog.db.connection_open_ms metric: nếu > 50ms → connection pool pressure