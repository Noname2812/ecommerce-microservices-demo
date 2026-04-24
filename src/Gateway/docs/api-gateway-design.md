# API Gateway — Design & Implementation Docs

> **Stack**: YARP (Yet Another Reverse Proxy) trên ASP.NET Core  
> **Client**: Web App (Browser / SPA)  
> **Pattern**: Gateway aggregator, JWT passthrough, RBAC enforcement  
> **Version**: 1.0

---

## Mục lục

1. [Trách nhiệm của API Gateway](#1-trách-nhiệm-của-api-gateway)
2. [Middleware Pipeline — thứ tự & lý do](#2-middleware-pipeline--thứ-tự--lý-do)
3. [Layer 1 — TLS Termination + CORS](#3-layer-1--tls-termination--cors)
4. [Layer 2 — Rate Limiting](#4-layer-2--rate-limiting)
5. [Layer 3 — JWT Authentication](#5-layer-3--jwt-authentication)
6. [Layer 4 — RBAC Permission Check](#6-layer-4--rbac-permission-check)
7. [Layer 5 — Request Transform](#7-layer-5--request-transform)
8. [Layer 6 — Logging + Distributed Tracing](#8-layer-6--logging--distributed-tracing)
9. [Layer 7 — YARP Reverse Proxy + Routing](#9-layer-7--yarp-reverse-proxy--routing)
10. [Route Table — toàn bộ hệ thống](#10-route-table--toàn-bộ-hệ-thống)
11. [Error Response Contract](#11-error-response-contract)
12. [Configuration (appsettings)](#12-configuration-appsettings)
13. [.NET Implementation Guide](#13-net-implementation-guide)
14. [Những gì Gateway KHÔNG làm](#14-những-gì-gateway-không-làm)

---

## 1. Trách nhiệm của API Gateway

### Gateway LÀM gì

```
✅ TLS termination          — HTTPS tại edge, nội bộ có thể HTTP
✅ CORS enforcement         — chỉ accept từ allowed origins
✅ Rate limiting            — bảo vệ downstream services
✅ JWT verification         — verify signature + expiry (stateless)
✅ RBAC coarse-grained      — check permission có trong token không
✅ Request enrichment       — inject X-User-Id, X-Merchant-Id, X-Request-Id
✅ Structured logging       — mọi request đều có correlation ID
✅ Distributed tracing      — OpenTelemetry propagation
✅ Reverse proxy + routing  — forward đến đúng service
✅ Health check aggregation — /health tổng hợp tình trạng các service
```

### Gateway KHÔNG LÀM gì

```
❌ Business logic           — không biết "Product X thuộc Merchant A không"
❌ Fine-grained authz       — ownership check do từng service tự làm
❌ Data transformation      — không reshape response
❌ Caching response         — không cache business data (chỉ cache JWK keys)
❌ Service discovery        — dùng static config hoặc Kubernetes DNS
❌ Circuit breaker          — YARP có built-in passive health check, đủ cho demo
❌ Aggregation / BFF        — không gom nhiều service calls thành 1 response
```

> **Nguyên tắc**: Gateway là "intelligent router", không phải "smart proxy". Business complexity
> nằm ở services, không nằm ở Gateway. Gateway mỏng = dễ debug, dễ scale.

---

## 2. Middleware Pipeline — thứ tự & lý do

Thứ tự middleware **không được đảo** — mỗi layer phụ thuộc vào layer trước:

```
Request đến
     │
     ▼
[1] TLS + CORS          ← Reject sớm nhất nếu origin không hợp lệ
     │                    Tiết kiệm CPU cho các bước sau
     ▼
[2] Rate Limiting        ← Trước Auth để block DDoS ngay cả anonymous traffic
     │                    Nếu để sau Auth: attacker có thể spam /login
     ▼
[3] JWT Authentication   ← Verify token (stateless, không gọi DB)
     │                    401 nếu không có token hoặc token invalid
     ▼
[4] RBAC Check           ← Cần user identity từ [3] để check permission
     │                    403 nếu không đủ quyền
     ▼
[5] Request Transform    ← Cần user claims từ [3] để inject headers
     │                    Enrich request trước khi forward
     ▼
[6] Logging + Tracing    ← Cần đầy đủ context (user, route, status) để log đúng
     │
     ▼
[7] YARP Proxy + Route   ← Forward đến service, handle response
     │
     ▼
Response về client
```

---

## 3. Layer 1 — TLS Termination + CORS

### TLS

- Gateway là điểm duy nhất terminate HTTPS từ browser
- Nội bộ giữa Gateway và services dùng HTTP (trong private network / Kubernetes cluster)
- Certificate: Let's Encrypt (production) hoặc self-signed (dev)

### CORS Policy

```
Allowed Origins:
  Production : https://yourapp.com, https://admin.yourapp.com
  Staging    : https://staging.yourapp.com
  Dev        : http://localhost:3000, http://localhost:5173

Allowed Methods : GET, POST, PUT, PATCH, DELETE, OPTIONS
Allowed Headers : Authorization, Content-Type, X-Request-Id
Exposed Headers : X-Request-Id, X-RateLimit-Remaining
Allow Credentials: true  ← cần thiết vì dùng httpOnly cookie cho refresh token
Max Age         : 3600 seconds (preflight cache)
```

### Rule quan trọng: credentials + wildcard origin không được dùng cùng nhau

```
❌ AllowAnyOrigin() + AllowCredentials()  → browser chặn
✅ WithOrigins("https://yourapp.com") + AllowCredentials()
```

---

## 4. Layer 2 — Rate Limiting

### Chiến lược: nhiều bucket, nhiều granularity

| Bucket | Key | Limit | Window | Áp dụng |
|---|---|---|---|---|
| Global IP | IP address | 1000 req | 1 phút | Tất cả routes |
| Auth endpoints | IP address | 10 req | 1 phút | `/auth/login`, `/auth/register`, `/auth/forgot-password` |
| API per user | User ID (từ JWT) | 300 req | 1 phút | Authenticated routes |
| API per merchant | Merchant ID | 500 req | 1 phút | Merchant routes |
| Search | IP / User | 60 req | 1 phút | `/search/*` |
| Write operations | User ID | 50 req | 1 phút | POST/PUT/DELETE |

### Algorithm: Sliding Window (Redis)

```
Tại sao Sliding Window thay vì Fixed Window?
Fixed Window: 100 req/phút → user có thể gửi 200 req trong 2 giây
  (100 cuối phút N + 100 đầu phút N+1)
Sliding Window: lúc nào cũng đúng 100 req trong 60 giây vừa qua
```

### Response headers trả về client

```
X-RateLimit-Limit     : 300
X-RateLimit-Remaining : 247
X-RateLimit-Reset     : 1706001234  (Unix timestamp khi window reset)
Retry-After           : 45          (chỉ có khi 429)
```

### Response khi bị rate limit

```json
HTTP 429 Too Many Requests
{
  "error": "RATE_LIMIT_EXCEEDED",
  "message": "Quá nhiều request. Vui lòng thử lại sau 45 giây.",
  "retry_after": 45
}
```

---

## 5. Layer 3 — JWT Authentication

### Verify process (stateless — không gọi DB, không gọi Auth Service)

```
1. Extract token từ:
   a. Authorization: Bearer {token}   ← ưu tiên
   b. Cookie: access_token            ← fallback nếu SPA dùng cookie

2. Decode header → lấy kid (Key ID)

3. Fetch public key từ JWK endpoint Auth Service:
   GET https://auth.yourapp.com/.well-known/jwks.json
   → Cache public key trong memory (TTL: 1 giờ)
   → Chỉ refetch nếu kid không tìm thấy trong cache (key rotation)

4. Verify signature (RS256)

5. Validate claims:
   iss  == "https://auth.yourapp.com"
   aud  == "api.yourapp.com"
   exp  > now (+ 30 giây clock skew tolerance)
   nbf  <= now

6. Nếu fail → 401 Unauthorized (không reveal lý do cụ thể)
```

### Public vs Protected routes

```
Public (không cần token):
  GET  /api/v1/products          Browse sản phẩm
  GET  /api/v1/products/{id}     Chi tiết sản phẩm
  GET  /api/v1/search/*          Tìm kiếm
  GET  /api/v1/categories        Danh mục
  POST /auth/register            Đăng ký
  POST /auth/login               Đăng nhập
  POST /oauth/token              Token exchange
  GET  /oauth/callback/*         Social login callback
  GET  /health                   Health check

Protected (cần valid JWT):
  Tất cả routes còn lại
```

### JWK Cache strategy

```
Cache-Aside pattern:
  1. Nhận token với kid="auth-key-2024-01"
  2. Tìm trong memory cache theo kid
  3. Cache hit   → dùng key đó verify
  4. Cache miss  → fetch từ /jwks.json → cache → verify
  5. Verify fail sau cache hit → refetch 1 lần (key có thể vừa rotate)
  6. Vẫn fail    → 401

TTL cache: 1 giờ (không quá ngắn → gây load cho Auth Service)
Max keys cached: 5 (key rotation không xảy ra thường xuyên)
```

---

## 6. Layer 4 — RBAC Permission Check

### Nguyên tắc: coarse-grained tại Gateway, fine-grained tại Service

```
Gateway check: "Token có chứa permission 'products:write:own' hoặc 'products:write:all' không?"
Service check: "product.seller_id == request.merchant_id không?" (ownership)

Gateway KHÔNG biết product đó của ai — chỉ biết user có permission type đó không.
```

### Route → Required Permission Mapping

```
GET    /api/v1/products          → public (no auth)
POST   /api/v1/products          → products:write:own  OR  products:write:all
PUT    /api/v1/products/{id}     → products:write:own  OR  products:write:all
DELETE /api/v1/products/{id}     → products:delete:own OR  products:delete:all

GET    /api/v1/inventory         → inventory:read:own  OR  inventory:read:all
PUT    /api/v1/inventory/{id}    → inventory:write:own OR  inventory:write:all

GET    /api/v1/orders            → orders:read:own     OR  orders:read:all
POST   /api/v1/orders            → authenticated (bất kỳ logged-in user)
PUT    /api/v1/orders/{id}       → orders:write:own    OR  orders:write:all
DELETE /api/v1/orders/{id}       → orders:cancel:own   OR  orders:cancel:all

GET    /api/v1/payments          → payments:read:own   OR  payments:read:all
POST   /api/v1/payments/refund   → payments:refund

GET    /admin/*                  → role: admin OR system_admin
GET    /system/*                 → role: system_admin
```

### Permission resolution logic

```
Token claims:
  "roles": ["merchant"],
  "permissions": ["products:write:own", "inventory:read:own", ...]

Check cho route PUT /api/v1/products/{id}:
  Required: products:write:own OR products:write:all

  Step 1: permissions contains "products:write:all"? → NO
  Step 2: permissions contains "products:write:own"? → YES → ALLOW
  Step 3: Forward với X-Permission-Scope: own
          (service biết cần check ownership)

Check cho role system_admin (wildcard):
  permissions contains "*:*:*"? → YES → ALLOW always
```

### Header truyền xuống service khi ALLOW

```
X-Permission-Scope : own   (hoặc "all" nếu Admin/SystemAdmin)
```

Service dựa vào `X-Permission-Scope: own` để biết cần enforce ownership check. Nếu `all` thì skip ownership check.

---

## 7. Layer 5 — Request Transform

Sau khi authenticate + authorize xong, Gateway enrich request với các headers để downstream services không cần re-parse JWT:

### Headers inject vào mọi downstream request

```
X-User-Id        : {uuid}          — identity của user
X-User-Roles     : merchant,customer  — comma-separated roles
X-Merchant-Id    : {uuid}          — chỉ có nếu user có role merchant (lấy từ JWT claim merchant_id)
X-Permission-Scope: own            — "own" hoặc "all" (xem Layer 4)
X-Request-Id     : {uuid v4}       — correlation ID cho distributed tracing
X-Forwarded-For  : {client IP}     — IP thực của client (sau proxy)
X-Forwarded-Host : yourapp.com     — original host
```

### Headers bị STRIP trước khi forward

```
Authorization    — KHÔNG forward JWT xuống service (service không cần verify lại)
Cookie           — KHÔNG forward cookies xuống service
```

> **Lý do strip Authorization**: Downstream services tin tưởng Gateway đã verify. Nếu forward JWT,
> service phải verify lại (wasteful) hoặc trust mà không verify (insecure). Dùng header riêng
> (`X-User-Id` v.v.) rõ ràng hơn, không thể bị client forge vì Gateway overwrite chúng.

### Path rewrite rules

```
Client gọi:                          Gateway forward đến:
/api/v1/products/*          →        product-service:8081/api/v1/products/*
/api/v1/inventory/*         →        inventory-service:8082/api/v1/inventory/*
/api/v1/orders/*            →        order-service:8083/api/v1/orders/*
/api/v1/payments/*          →        payment-service:8084/api/v1/payments/*
/api/v1/search/*            →        search-service:8085/api/v1/search/*
/auth/*                     →        auth-service:8086/auth/*
/oauth/*                    →        auth-service:8086/oauth/*
/admin/*                    →        [route đến service tương ứng, check admin role]
```

---

## 8. Layer 6 — Logging + Distributed Tracing

### Structured Log format (mỗi request)

```json
{
  "timestamp":      "2024-01-23T10:30:00.123Z",
  "level":          "Information",
  "request_id":     "550e8400-e29b-41d4-a716-446655440000",
  "trace_id":       "4bf92f3577b34da6a3ce929d0e0e4736",
  "span_id":        "00f067aa0ba902b7",
  "method":         "PUT",
  "path":           "/api/v1/products/abc-123",
  "query":          "",
  "status_code":    200,
  "duration_ms":    45,
  "user_id":        "user-uuid",
  "merchant_id":    "merchant-uuid",
  "roles":          "merchant",
  "permission":     "products:write:own",
  "upstream":       "product-service",
  "upstream_ms":    38,
  "client_ip":      "1.2.3.4",
  "user_agent":     "Mozilla/5.0...",
  "error":          null
}
```

### Không log những field nhạy cảm

```
❌ Authorization header (có JWT)
❌ Cookie values
❌ Request body (có thể chứa password, card number)
❌ Response body
✅ Request path, method, status, duration  → luôn log
✅ User ID, Merchant ID                    → luôn log (không PII nhạy cảm)
✅ Error messages                          → log khi status >= 400
```

### Distributed Tracing — OpenTelemetry

```
Gateway nhận request:
  → Tạo TraceId mới (nếu không có W3C Trace-Context header)
  → Tạo SpanId cho gateway span
  → Inject traceparent header trước khi forward:
    traceparent: 00-{trace_id}-{span_id}-01

Downstream service nhận:
  → Parse traceparent
  → Tạo child span với parent = gateway span
  → Tất cả logs trong service đều có cùng trace_id

Kết quả: có thể trace 1 request xuyên suốt qua tất cả services
```

---

## 9. Layer 7 — YARP Reverse Proxy + Routing

### Load balancing (cho production scale)

```
RoundRobin    : default cho stateless services
LeastRequests : cho services có response time biến động (search, payment)
```

### Health check (passive)

```
YARP Passive Health Check:
  - Monitor response status từ upstream
  - Nếu service trả về 5xx liên tục → đánh dấu unhealthy
  - Không forward traffic đến unhealthy instance
  - Tự động recover khi service trả về 2xx trở lại

Active Health Check (optional):
  - GET {service}/health mỗi 30 giây
  - Expect: 200 OK với { "status": "healthy" }
```

### Timeout config

```
Gateway → Service timeout: 30 giây (default)
Exception:
  /api/v1/search/*  : 10 giây (ES query nên nhanh)
  /api/v1/payments/process : 60 giây (external payment provider có thể chậm)
```

### Retry policy (idempotent only)

```
Retry: 2 lần, với backoff 100ms, 500ms
Chỉ retry: GET requests và 502/503/504 status
KHÔNG retry: POST/PUT/DELETE (có thể gây duplicate)
```

---

## 10. Route Table — toàn bộ hệ thống

### Public Routes (không cần Auth)

| Method | Path | Upstream | Ghi chú |
|---|---|---|---|
| GET | `/api/v1/products` | product-service | Browse catalog |
| GET | `/api/v1/products/{id}` | product-service | Product detail |
| GET | `/api/v1/products/{id}/variants` | product-service | Variants |
| GET | `/api/v1/categories` | product-service | Category tree |
| GET | `/api/v1/search` | search-service | Full-text search |
| POST | `/auth/register` | auth-service | Rate limit: 10/min/IP |
| POST | `/auth/login` | auth-service | Rate limit: 10/min/IP |
| POST | `/auth/forgot-password` | auth-service | Rate limit: 5/min/IP |
| POST | `/auth/reset-password` | auth-service | |
| GET | `/auth/verify-email` | auth-service | |
| POST | `/oauth/token` | auth-service | Rate limit: 20/min/IP |
| GET | `/oauth/authorize` | auth-service | |
| GET | `/oauth/callback/{provider}` | auth-service | Google, Facebook |
| GET | `/oauth/.well-known/*` | auth-service | OIDC discovery |
| GET | `/health` | gateway itself | Aggregate health |

### Customer Routes (cần JWT, role: customer+)

| Method | Path | Required Permission | Upstream |
|---|---|---|---|
| GET | `/api/v1/orders` | `orders:read:own` | order-service |
| POST | `/api/v1/orders` | authenticated | order-service |
| GET | `/api/v1/orders/{id}` | `orders:read:own` | order-service |
| DELETE | `/api/v1/orders/{id}` | `orders:cancel:own` | order-service |
| GET | `/api/v1/payments` | `payments:read:own` | payment-service |
| GET | `/api/v1/payments/{id}` | `payments:read:own` | payment-service |
| POST | `/api/v1/payments/checkout` | authenticated | payment-service |
| GET | `/auth/me` | authenticated | auth-service |
| PATCH | `/auth/me` | authenticated | auth-service |
| POST | `/auth/me/change-password` | authenticated | auth-service |
| GET | `/auth/me/sessions` | authenticated | auth-service |
| DELETE | `/auth/me/sessions/{id}` | authenticated | auth-service |
| POST | `/auth/logout` | authenticated | auth-service |
| POST | `/auth/logout/all` | authenticated | auth-service |
| POST | `/auth/mfa/setup` | authenticated | auth-service |
| POST | `/oauth/mfa/verify` | authenticated | auth-service |

### Merchant Routes (cần JWT, role: merchant)

| Method | Path | Required Permission | Upstream |
|---|---|---|---|
| GET | `/api/v1/products/mine` | `products:read:own` | product-service |
| POST | `/api/v1/products` | `products:write:own` | product-service |
| PUT | `/api/v1/products/{id}` | `products:write:own` | product-service |
| DELETE | `/api/v1/products/{id}` | `products:delete:own` | product-service |
| GET | `/api/v1/inventory` | `inventory:read:own` | inventory-service |
| PUT | `/api/v1/inventory/{id}` | `inventory:write:own` | inventory-service |
| GET | `/api/v1/orders/store` | `orders:read:own` | order-service |
| PUT | `/api/v1/orders/{id}/ship` | `orders:write:own` | order-service |
| GET | `/api/v1/payments/revenue` | `payments:read:own` | payment-service |
| GET | `/merchants/me` | authenticated | auth-service |
| PATCH | `/merchants/me` | authenticated | auth-service |
| GET | `/merchants/me/oauth-clients` | authenticated | auth-service |
| POST | `/merchants/me/oauth-clients` | authenticated | auth-service |

### Admin Routes (cần JWT, role: admin+)

| Method | Path | Required Permission | Upstream |
|---|---|---|---|
| GET | `/admin/users` | `users:read:all` | auth-service |
| GET | `/admin/users/{id}` | `users:read:all` | auth-service |
| PATCH | `/admin/users/{id}` | `users:write:all` | auth-service |
| POST | `/admin/users/{id}/deactivate` | `users:write:all` | auth-service |
| POST | `/admin/users/{id}/roles` | `users:role:assign` | auth-service |
| GET | `/admin/merchants` | `merchants:read:all` | auth-service |
| POST | `/admin/merchants/{id}/approve` | `merchants:approve` | auth-service |
| POST | `/admin/merchants/{id}/suspend` | `merchants:suspend` | auth-service |
| GET | `/admin/products` | `products:read:all` | product-service |
| DELETE | `/admin/products/{id}` | `products:delete:all` | product-service |
| GET | `/admin/orders` | `orders:read:all` | order-service |
| POST | `/admin/orders/{id}/refund` | `orders:refund` | order-service |
| GET | `/admin/audit-logs` | `system:audit:read` | auth-service |

### SystemAdmin Routes (cần JWT, role: system_admin + mfa_verified)

| Method | Path | Required Permission | Upstream |
|---|---|---|---|
| GET | `/system/config` | `system:config:read` | auth-service |
| PUT | `/system/config` | `system:config:write` | auth-service |
| GET | `/system/oauth-clients` | `system:oauth:manage` | auth-service |
| POST | `/system/oauth-clients` | `system:oauth:manage` | auth-service |
| GET | `/system/roles` | `system:rbac:manage` | auth-service |
| POST | `/system/roles/{id}/permissions` | `system:rbac:manage` | auth-service |

---

## 11. Error Response Contract

Gateway trả về error responses theo format chuẩn cho **tất cả** lỗi ở tầng gateway (trước khi forward):

```json
{
  "request_id": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp":  "2024-01-23T10:30:00Z",
  "error":      "UNAUTHORIZED",
  "message":    "Token không hợp lệ hoặc đã hết hạn.",
  "details":    null
}
```

### Error codes tại Gateway

| HTTP | Error Code | Tình huống |
|---|---|---|
| 400 | `BAD_REQUEST` | Request malformed (header thiếu, format sai) |
| 401 | `UNAUTHORIZED` | Không có token, token invalid, token expired |
| 401 | `TOKEN_EXPIRED` | Token hết hạn (client cần refresh) |
| 403 | `FORBIDDEN` | Token hợp lệ nhưng không đủ permission |
| 403 | `MFA_REQUIRED` | Route yêu cầu MFA nhưng token chưa có `mfa_verified: true` |
| 404 | `ROUTE_NOT_FOUND` | Path không tồn tại trong route table |
| 429 | `RATE_LIMIT_EXCEEDED` | Vượt rate limit |
| 502 | `UPSTREAM_ERROR` | Service downstream trả về lỗi |
| 503 | `SERVICE_UNAVAILABLE` | Service downstream không available |
| 504 | `GATEWAY_TIMEOUT` | Service downstream timeout |

> **Lưu ý**: Gateway KHÔNG wrap lỗi từ downstream services. Nếu Product Service trả về
> `{ "error": "PRODUCT_NOT_FOUND" }` với status 404, Gateway forward nguyên response đó.
> Gateway chỉ tạo error response cho các lỗi xảy ra **tại gateway** (auth, rate limit, routing).

---

## 12. Configuration (appsettings)

### appsettings.json structure

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://yourapp.com",
      "https://admin.yourapp.com",
      "http://localhost:3000"
    ]
  },

  "RateLimit": {
    "GlobalPerIp": { "PermitLimit": 1000, "WindowSeconds": 60 },
    "AuthEndpoints": { "PermitLimit": 10,   "WindowSeconds": 60 },
    "AuthenticatedUser": { "PermitLimit": 300, "WindowSeconds": 60 },
    "SearchEndpoints": { "PermitLimit": 60, "WindowSeconds": 60 }
  },

  "Jwt": {
    "Issuer":   "https://auth.yourapp.com",
    "Audience": "api.yourapp.com",
    "JwksUri":  "https://auth.yourapp.com/.well-known/jwks.json",
    "JwksCacheTtlMinutes": 60,
    "ClockSkewSeconds": 30
  },

  "ReverseProxy": {
    "Routes": {
      "product-route": {
        "ClusterId": "product-cluster",
        "Match": { "Path": "/api/v1/products/{**catch-all}" },
        "Transforms": [{ "PathPattern": "/api/v1/products/{**catch-all}" }]
      },
      "inventory-route": {
        "ClusterId": "inventory-cluster",
        "Match": { "Path": "/api/v1/inventory/{**catch-all}" }
      },
      "order-route": {
        "ClusterId": "order-cluster",
        "Match": { "Path": "/api/v1/orders/{**catch-all}" }
      },
      "payment-route": {
        "ClusterId": "payment-cluster",
        "Match": { "Path": "/api/v1/payments/{**catch-all}" }
      },
      "search-route": {
        "ClusterId": "search-cluster",
        "Match": { "Path": "/api/v1/search/{**catch-all}" }
      },
      "auth-route": {
        "ClusterId": "auth-cluster",
        "Match": { "Path": "/auth/{**catch-all}" }
      },
      "oauth-route": {
        "ClusterId": "auth-cluster",
        "Match": { "Path": "/oauth/{**catch-all}" }
      }
    },
    "Clusters": {
      "product-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "product-1": { "Address": "http://product-service:8081" }
        },
        "HealthCheck": {
          "Passive": { "Enabled": true },
          "Active":  { "Enabled": true, "Path": "/health", "Interval": "00:00:30" }
        },
        "HttpRequest": { "Timeout": "00:00:30" }
      }
    }
  }
}
```

---

## 13. .NET Implementation Guide

### 13.1 Program.cs — pipeline setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// --- Services ---
builder.Services
    .AddCors(opt => opt.AddPolicy("WebApp", policy =>
        policy
            .WithOrigins(builder.Configuration
                .GetSection("Cors:AllowedOrigins").Get<string[]>()!)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("X-Request-Id", "X-RateLimit-Remaining")))
    .AddRateLimiter(opt => ConfigureRateLimiting(opt, builder.Configuration))
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => ConfigureJwtBearer(opt, builder.Configuration));

builder.Services
    .AddSingleton<IJwksCache, JwksCache>()
    .AddSingleton<IPermissionRegistry, PermissionRegistry>()
    .AddScoped<IRequestEnricher, RequestEnricher>()
    .AddOpenTelemetry().WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// --- Pipeline (thứ tự quan trọng) ---
var app = builder.Build();

app.UseHttpsRedirection();                  // [1] TLS
app.UseCors("WebApp");                      // [1] CORS
app.UseRateLimiter();                       // [2] Rate Limiting
app.UseAuthentication();                    // [3] JWT verify
app.UseMiddleware<RbacMiddleware>();         // [4] RBAC check
app.UseMiddleware<RequestEnrichmentMiddleware>(); // [5] Inject headers
app.UseMiddleware<StructuredLoggingMiddleware>(); // [6] Logging
app.MapReverseProxy();                      // [7] YARP forward

app.Run();
```

### 13.2 RBAC Middleware

```csharp
public class RbacMiddleware(RequestDelegate next, IPermissionRegistry registry)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var route = ctx.Request.Path.Value ?? "";
        var method = ctx.Request.Method;

        // Public routes — skip RBAC
        if (registry.IsPublicRoute(method, route))
        {
            await next(ctx);
            return;
        }

        // Must be authenticated
        if (!ctx.User.Identity?.IsAuthenticated ?? true)
        {
            ctx.Response.StatusCode = 401;
            await WriteError(ctx, "UNAUTHORIZED", "Authentication required.");
            return;
        }

        // Check required permission for route
        var requirement = registry.GetRequirement(method, route);
        if (requirement == null)
        {
            // Route cần auth nhưng không có permission requirement cụ thể
            // → Chỉ cần authenticated là đủ
            await next(ctx);
            return;
        }

        var permissions = ctx.User.FindFirst("permissions")?.Value;
        var userPerms   = permissions != null
            ? JsonSerializer.Deserialize<string[]>(permissions) ?? []
            : Array.Empty<string>();

        // SystemAdmin wildcard
        if (userPerms.Contains("*:*:*"))
        {
            ctx.Items["PermissionScope"] = "all";
            await next(ctx);
            return;
        }

        // Check :all scope (Admin)
        if (userPerms.Contains(requirement.AllScope))
        {
            ctx.Items["PermissionScope"] = "all";
            await next(ctx);
            return;
        }

        // Check :own scope (Merchant/Customer)
        if (userPerms.Contains(requirement.OwnScope))
        {
            ctx.Items["PermissionScope"] = "own";
            await next(ctx);
            return;
        }

        // Check MFA requirement
        if (requirement.RequiresMfa)
        {
            var mfaVerified = ctx.User.FindFirst("mfa_verified")?.Value == "true";
            if (!mfaVerified)
            {
                ctx.Response.StatusCode = 403;
                await WriteError(ctx, "MFA_REQUIRED", "Thao tác này yêu cầu xác thực MFA.");
                return;
            }
        }

        ctx.Response.StatusCode = 403;
        await WriteError(ctx, "FORBIDDEN", "Không có quyền truy cập.");
    }

    private static Task WriteError(HttpContext ctx, string code, string message)
    {
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new
        {
            request_id = ctx.TraceIdentifier,
            timestamp  = DateTime.UtcNow,
            error      = code,
            message
        });
        return ctx.Response.WriteAsync(body);
    }
}
```

### 13.3 Request Enrichment Middleware

```csharp
public class RequestEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        // Tạo hoặc lấy correlation ID
        var requestId = ctx.Request.Headers["X-Request-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();
        ctx.Response.Headers["X-Request-Id"] = requestId;
        ctx.TraceIdentifier = requestId;

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var userId     = ctx.User.FindFirst("sub")?.Value;
            var merchantId = ctx.User.FindFirst("merchant_id")?.Value;
            var roles      = ctx.User.FindFirst("roles")?.Value;
            var scope      = ctx.Items["PermissionScope"]?.ToString() ?? "own";

            // Inject vào request để YARP forward xuống service
            ctx.Request.Headers["X-User-Id"]          = userId;
            ctx.Request.Headers["X-Merchant-Id"]      = merchantId ?? "";
            ctx.Request.Headers["X-User-Roles"]       = roles ?? "";
            ctx.Request.Headers["X-Permission-Scope"] = scope;
            ctx.Request.Headers["X-Request-Id"]       = requestId;

            // STRIP sensitive headers — không forward xuống service
            ctx.Request.Headers.Remove("Authorization");
            ctx.Request.Headers.Remove("Cookie");
        }
        else
        {
            // Anonymous request — chỉ inject request ID
            ctx.Request.Headers["X-Request-Id"] = requestId;
            ctx.Request.Headers.Remove("Authorization");
            ctx.Request.Headers.Remove("Cookie");
        }

        await next(ctx);
    }
}
```

### 13.4 Rate Limiter config

```csharp
static void ConfigureRateLimiting(RateLimiterOptions opt, IConfiguration config)
{
    opt.RejectionStatusCode = 429;
    opt.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ts)
            ? (int)ts.TotalSeconds : 60;
        context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error      = "RATE_LIMIT_EXCEEDED",
            message    = $"Quá nhiều request. Vui lòng thử lại sau {retryAfter} giây.",
            retry_after = retryAfter
        }, ct);
    };

    // Global per IP (sliding window)
    opt.AddPolicy("global-ip", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit         = config.GetValue<int>("RateLimit:GlobalPerIp:PermitLimit"),
                Window              = TimeSpan.FromSeconds(config.GetValue<int>("RateLimit:GlobalPerIp:WindowSeconds")),
                SegmentsPerWindow   = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit          = 0
            }));

    // Auth endpoints (stricter)
    opt.AddPolicy("auth-endpoints", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit       = config.GetValue<int>("RateLimit:AuthEndpoints:PermitLimit"),
                Window            = TimeSpan.FromSeconds(60),
                SegmentsPerWindow = 6,
                QueueLimit        = 0
            }));

    // Per authenticated user
    opt.AddPolicy("per-user", ctx =>
    {
        var userId = ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: userId,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit       = config.GetValue<int>("RateLimit:AuthenticatedUser:PermitLimit"),
                Window            = TimeSpan.FromSeconds(60),
                SegmentsPerWindow = 6,
                QueueLimit        = 0
            });
    });
}
```

### 13.5 JWK Cache

```csharp
public class JwksCache(IHttpClientFactory httpClientFactory, IConfiguration config) : IJwksCache
{
    private readonly ConcurrentDictionary<string, (JsonWebKey Key, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(
        config.GetValue<int>("Jwt:JwksCacheTtlMinutes", 60));

    public async Task<JsonWebKey?> GetKeyAsync(string kid, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(kid, out var cached) && DateTime.UtcNow - cached.CachedAt < _ttl)
            return cached.Key;

        // Refetch
        var client = httpClientFactory.CreateClient();
        var uri    = config["Jwt:JwksUri"]!;
        var json   = await client.GetStringAsync(uri, ct);
        var jwks   = JsonWebKeySet.Create(json);

        foreach (var key in jwks.Keys)
            _cache[key.Kid] = (key, DateTime.UtcNow);

        return _cache.TryGetValue(kid, out var found) ? found.Key : null;
    }
}
```

---

## 14. Những gì Gateway KHÔNG làm

Phần này quan trọng để tránh "scope creep" — Gateway phình to thành một monolith:

```
❌ Không validate business rules
   Ví dụ: "Order có belong to Customer này không?"
   → Product Service, Order Service tự check

❌ Không aggregate multiple service responses
   Ví dụ: GET /api/v1/products/{id} trả về cả product + inventory + reviews
   → Client gọi riêng từng endpoint, hoặc cần BFF layer riêng

❌ Không cache business data
   Ví dụ: cache product list 5 phút
   → Đây là trách nhiệm của từng service (Redis cache tại service level)
   → Gateway chỉ cache JWK public keys (infrastructure concern)

❌ Không transform/reshape response body
   Ví dụ: rename fields, filter fields theo role
   → Downstream services trả về đúng format

❌ Không handle retries cho POST/PUT/DELETE
   → Retry non-idempotent requests = nguy cơ duplicate data
   → Chỉ retry GET + 5xx từ upstream

❌ Không implement circuit breaker phức tạp
   → YARP passive health check đủ cho demo
   → Production scale: cân nhắc Polly hoặc Resilience4j

❌ Không làm service discovery
   → Dùng static config (dev) hoặc Kubernetes DNS (production)
   → Kubernetes: service name = DNS, không cần service registry
```

---

## Tóm tắt: Gateway = 7 việc, không hơn

```
1. TLS + CORS          → Security ở tầng transport
2. Rate Limiting       → Bảo vệ infrastructure
3. JWT Verify          → "Token này có hợp lệ không?"
4. RBAC Check          → "User có permission type đúng không?"
5. Header Enrichment   → "Thêm context cho downstream services"
6. Structured Logging  → "Mọi request đều traceable"
7. Reverse Proxy       → "Forward đến đúng nơi"
```

---

*Document version 1.0 — API Gateway Design với YARP .NET*  
*Ecommerce Microservices*
