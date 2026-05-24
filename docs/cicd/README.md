# CI/CD — UrbanX trên VPS Ubuntu 22.04 với Jenkins

Bộ docs này hướng dẫn từng bước dựng pipeline CI/CD cho UrbanX: dev push code lên branch `develop` → Jenkins tự động build, test, đóng gói Docker image, push lên Docker Hub và deploy xuống VPS staging.

---

## Quyết định kiến trúc

| Lựa chọn | Giá trị | Lý do |
|---|---|---|
| Deploy target | Docker Compose trên VPS Ubuntu 22.04 | Đơn giản, đủ cho 1 node, tốc độ deploy nhanh |
| Container registry | Docker Hub | Free, dễ setup, không phụ thuộc hạ tầng tự host |
| CI host | VPS riêng (Jenkins VPS) | Tách build khỏi runtime, an toàn hơn |
| Trigger | Push `develop` → build + deploy staging | CD đầy đủ cho môi trường staging |

> Nếu sau này lên production thực, copy folder này thành `docs/cicd-prod/` và thêm 1 VPS prod + branch `main` + manual approval gate.

---

## Sơ đồ kiến trúc

```
                       ┌───────────────────────┐
                       │   GitHub Repository   │
                       │   branch: develop     │
                       └──────────┬────────────┘
                                  │ webhook (push)
                                  ▼
┌─────────────────────────────────────────────────────────┐
│   Jenkins VPS  (Ubuntu 22.04)                           │
│   - Jenkins LTS (Docker)                                │
│   - .NET 10 SDK (trong agent image)                     │
│   - Docker CLI để build image                           │
│   Pipeline stages:                                      │
│   ① Checkout  ② Restore  ③ Build  ④ Test               │
│   ⑤ Docker build (parallel per service)                 │
│   ⑥ Push Docker Hub  ⑦ Build efbundle (EF migrations)   │
│   ⑧ SSH → Deploy VPS  ⑨ Smoke test                      │
└──────────┬────────────────────────────────────┬─────────┘
           │ docker push                        │ ssh
           ▼                                    ▼
┌──────────────────────────┐    ┌────────────────────────────────────┐
│  Docker Hub              │    │  Deploy VPS Staging (Ubuntu 22.04) │
│  urbanx-gateway:tag      │◄───┤  - Docker + Compose plugin         │
│  urbanx-catalog:tag      │    │  - /opt/urbanx/                    │
│  urbanx-identity:tag     │    │     docker-compose.staging.yml     │
│  urbanx-inventory:tag    │    │     .env.staging                   │
│  urbanx-efbundles:tag    │    │  - PostgreSQL, RabbitMQ, Redis     │
└──────────────────────────┘    │  - UrbanX services (containers)    │
                                │  - Nginx reverse proxy + TLS       │
                                └────────────────────────────────────┘
```

---

## Roadmap — đọc theo thứ tự

| Bước | File | Mục tiêu |
|---|---|---|
| 1 | [01-prerequisites.md](01-prerequisites.md) | Chuẩn bị tài khoản, VPS, domain |
| 2 | [02-deploy-vps-setup.md](02-deploy-vps-setup.md) | Setup VPS staging (Docker, firewall, user) |
| 3 | [03-jenkins-vps-setup.md](03-jenkins-vps-setup.md) | Cài Jenkins LTS + .NET SDK trong container |
| 4 | [04-jenkins-credentials.md](04-jenkins-credentials.md) | Khai báo credentials, plugin, SSH key |
| 5 | [05-dockerfiles.md](05-dockerfiles.md) | Viết Dockerfile chuẩn cho từng service |
| 6 | [06-docker-compose-staging.md](06-docker-compose-staging.md) | Compose file + biến môi trường staging |
| 7 | [07-jenkinsfile.md](07-jenkinsfile.md) | Pipeline declarative đầy đủ |
| 8 | [08-database-migrations.md](08-database-migrations.md) | Chạy EF Core migrations qua `efbundle` |
| 9 | [09-github-webhook.md](09-github-webhook.md) | Webhook GitHub → Jenkins |
| 10 | [10-rollback-monitoring.md](10-rollback-monitoring.md) | Rollback, logs, health, troubleshooting |

---

## Quy ước trong docs

- Lệnh chạy trên **Jenkins VPS**: prompt `jenkins@ci:~$`
- Lệnh chạy trên **Deploy VPS**: prompt `deploy@staging:~$`
- Lệnh chạy trên **máy dev local**: prompt `$`
- Placeholder bao bằng `<...>` — thay bằng giá trị thật khi áp dụng.

## Tham chiếu cấu trúc project hiện tại

Active services (đang scaffold): **Catalog, Identity, Inventory, Gateway**.
Disabled (chưa active trong AppHost): Order, Payment, Merchant — docs này chỉ bao 4 service active.
