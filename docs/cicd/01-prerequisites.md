# Bước 1 — Prerequisites

Checklist cần chuẩn bị trước khi bắt đầu.

---

## 1. Tài khoản online

| Dịch vụ | Mục đích | Free tier |
|---|---|---|
| [Docker Hub](https://hub.docker.com/signup) | Lưu image | 1 private repo + unlimited public |
| [GitHub](https://github.com) | Source code + webhook | Đã có |
| Nhà cung cấp VPS | Chạy Jenkins + Deploy | Vultr, DigitalOcean, Hetzner, Contabo… |
| Domain (optional) | URL Jenkins + staging | Namecheap, Cloudflare Registrar… |

---

## 2. VPS — 2 server Ubuntu 22.04 LTS

### Jenkins VPS

| Thông số | Khuyến nghị tối thiểu |
|---|---|
| CPU | 2 vCPU |
| RAM | 4 GB |
| Disk | 40 GB SSD |
| OS | Ubuntu 22.04 LTS |
| IPv4 public | Có |

Lý do RAM 4GB: Jenkins + JVM + `dotnet build` đồng thời ngốn RAM. Build .NET 10 song song nhiều project rất tốn bộ nhớ.

### Deploy VPS (Staging)

| Thông số | Khuyến nghị tối thiểu |
|---|---|
| CPU | 2 vCPU (4 nếu chạy đủ services + DB) |
| RAM | 8 GB |
| Disk | 60 GB SSD |
| OS | Ubuntu 22.04 LTS |
| IPv4 public | Có |

Lý do RAM 8GB: 4 .NET services + Postgres + RabbitMQ + Redis + Nginx. Mỗi .NET runtime ngốn 200–400 MB lúc idle.

> Nếu budget hạn chế: gộp 2 vai trò vào 1 VPS 8GB. Trade-off: build .NET sẽ làm services staging giật. Docs này giả định 2 VPS riêng.

---

## 3. Domain & DNS (khuyến nghị, không bắt buộc)

Khuyến nghị đăng ký 1 domain (ví dụ `urbanx.dev`) và trỏ 2 subdomain A record:

| Record | Trỏ về | Mục đích |
|---|---|---|
| `ci.urbanx.dev` | IP Jenkins VPS | Truy cập Jenkins UI qua HTTPS |
| `staging.urbanx.dev` | IP Deploy VPS | URL gateway public của staging |
| `api.staging.urbanx.dev` | IP Deploy VPS | Optional, alias gateway |

Không có domain vẫn dùng IP + port được, nhưng GitHub webhook sẽ phải dùng IP và Jenkins sẽ không HTTPS được — bất tiện.

---

## 4. Source code

- Repo GitHub có quyền tạo webhook (admin).
- Branch `develop` đã tồn tại (hoặc sẽ tạo).
- Cấu trúc project khớp với `CLAUDE.md` ở root.

---

## 5. SSH keys

Cần 2 cặp SSH key — sinh trên máy local trước:

```bash
# Key dùng để admin SSH lên cả 2 VPS
$ ssh-keygen -t ed25519 -C "admin@urbanx" -f ~/.ssh/urbanx_admin

# Key dùng cho Jenkins SSH từ Jenkins VPS → Deploy VPS để deploy
$ ssh-keygen -t ed25519 -C "jenkins@urbanx" -f ~/.ssh/urbanx_jenkins_deploy
```

Lát nữa:
- `urbanx_admin.pub` → add vào `~/.ssh/authorized_keys` của user admin trên cả 2 VPS.
- `urbanx_jenkins_deploy.pub` → add vào `authorized_keys` của user `deploy` trên Deploy VPS.
- `urbanx_jenkins_deploy` (private) → upload vào Jenkins Credentials.

---

## 6. Bảng biến môi trường

Chuẩn bị sẵn (chưa fill được hết, sẽ điền dần):

```ini
# Identity / Domain
DOMAIN_CI=ci.urbanx.dev
DOMAIN_STAGING=staging.urbanx.dev

# Docker Hub
DOCKER_HUB_USERNAME=<your-dockerhub-username>
DOCKER_HUB_TOKEN=<sẽ tạo ở bước 4>

# Postgres staging
POSTGRES_USER=urbanx_app
POSTGRES_PASSWORD=<chuỗi 32 ký tự random>

# RabbitMQ staging
RABBITMQ_USER=urbanx
RABBITMQ_PASSWORD=<chuỗi 32 ký tự random>

# JWT / Identity
IDENTITY_SIGNING_KEY=<RSA private key hoặc base64 secret>

# SMTP (optional, dev dùng LogEmailSender)
# Skip nếu chưa cần
```

Sinh password ngẫu nhiên trên Linux:

```bash
$ openssl rand -base64 32
```

---

## Done?

Tick xong checklist này thì sang [02-deploy-vps-setup.md](02-deploy-vps-setup.md).
