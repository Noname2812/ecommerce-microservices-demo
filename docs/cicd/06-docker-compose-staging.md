# Bước 6 — Docker Compose Staging

Mục tiêu: file `docker-compose.staging.yml` đặt tại `/opt/urbanx/compose/` trên Deploy VPS, dùng pull image từ Docker Hub, mount volume cho data, healthcheck đầy đủ.

---

## 6.1. Cấu trúc thư mục trên Deploy VPS

```
/opt/urbanx/
├── compose/
│   └── docker-compose.staging.yml        ← do Jenkins SCP lên
├── env/
│   └── .env.staging                      ← do Jenkins SCP lên (chmod 600)
├── bundles/
│   ├── catalog-efbundle                  ← EF migrations bundle
│   ├── identity-efbundle
│   └── inventory-efbundle
├── data/                                 ← named volumes mount vào đây (qua docker volume)
│   ├── postgres/
│   ├── rabbitmq/
│   └── redis/
└── backups/
```

Khuyến nghị: dùng `docker volume` (named) thay vì bind mount `./data/postgres` để Docker tự quản — đỡ phải lo permission UID/GID.

---

## 6.2. File `docker-compose.staging.yml`

Đặt tại `/opt/urbanx/compose/docker-compose.staging.yml`. Đây là file Jenkins copy lên mỗi lần deploy.

```yaml
name: urbanx-staging

x-default-logging: &default-logging
  driver: json-file
  options:
    max-size: "20m"
    max-file: "5"

x-aspnet-base: &aspnet-base
  restart: unless-stopped
  logging: *default-logging
  env_file:
    - /opt/urbanx/env/.env.staging
  networks:
    - urbanx-net
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Staging}
    DOTNET_RUNNING_IN_CONTAINER: "true"
    OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_EXPORTER_OTLP_ENDPOINT:-}
  depends_on:
    postgres:
      condition: service_healthy
    rabbitmq:
      condition: service_healthy
    redis:
      condition: service_healthy

services:
  # ─────────── Infrastructure ───────────

  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    logging: *default-logging
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_MULTIPLE_DATABASES: "${POSTGRES_DB_CATALOG},${POSTGRES_DB_IDENTITY},${POSTGRES_DB_INVENTORY}"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init-multi-db.sh:/docker-entrypoint-initdb.d/init-multi-db.sh:ro
    networks:
      - urbanx-net
    # Không expose 5432 ra host — chỉ Docker network
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER}"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    hostname: urbanx-rabbit
    restart: unless-stopped
    logging: *default-logging
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - urbanx-net
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 15s
      timeout: 10s
      retries: 5

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    logging: *default-logging
    command:
      - "redis-server"
      - "--requirepass"
      - "${REDIS_PASSWORD}"
      - "--maxmemory"
      - "256mb"
      - "--maxmemory-policy"
      - "allkeys-lru"
    volumes:
      - redis_data:/data
    networks:
      - urbanx-net
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "${REDIS_PASSWORD}", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  # ─────────── Services ───────────

  gateway:
    <<: *aspnet-base
    image: ${DOCKER_HUB_USERNAME}/urbanx-gateway:${IMAGE_TAG}
    ports:
      - "127.0.0.1:5000:8080"      # Nginx host proxy vào 5000
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Staging}
      ASPNETCORE_URLS: http://+:8080
      ReverseProxy__Clusters__catalog__Destinations__d1__Address: http://catalog:8080/
      ReverseProxy__Clusters__identity__Destinations__d1__Address: http://identity:8080/
      ReverseProxy__Clusters__inventory__Destinations__d1__Address: http://inventory:8080/
    depends_on:
      catalog:
        condition: service_healthy
      identity:
        condition: service_healthy
      inventory:
        condition: service_healthy

  catalog:
    <<: *aspnet-base
    image: ${DOCKER_HUB_USERNAME}/urbanx-catalog:${IMAGE_TAG}
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Staging}
      ConnectionStrings__catalogdb: "Host=postgres;Port=5432;Database=${POSTGRES_DB_CATALOG};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__rabbitmq: "amqp://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      ConnectionStrings__redis: "redis:6379,password=${REDIS_PASSWORD}"
    healthcheck:
      test: ["CMD", "curl", "--fail", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 5
      start_period: 60s

  identity:
    <<: *aspnet-base
    image: ${DOCKER_HUB_USERNAME}/urbanx-identity:${IMAGE_TAG}
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Staging}
      ConnectionStrings__identitydb: "Host=postgres;Port=5432;Database=${POSTGRES_DB_IDENTITY};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__rabbitmq: "amqp://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      ConnectionStrings__redis: "redis:6379,password=${REDIS_PASSWORD}"
      IdentityServer__IssuerUri: ${IDENTITY_ISSUER_URI}
      IdentityServer__SigningKey: ${IDENTITY_SIGNING_KEY}
    healthcheck:
      test: ["CMD", "curl", "--fail", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 5
      start_period: 60s

  inventory:
    <<: *aspnet-base
    image: ${DOCKER_HUB_USERNAME}/urbanx-inventory:${IMAGE_TAG}
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Staging}
      ConnectionStrings__inventorydb: "Host=postgres;Port=5432;Database=${POSTGRES_DB_INVENTORY};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__rabbitmq: "amqp://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      ConnectionStrings__redis: "redis:6379,password=${REDIS_PASSWORD}"
    healthcheck:
      test: ["CMD", "curl", "--fail", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 5
      start_period: 60s

volumes:
  postgres_data:
  rabbitmq_data:
  redis_data:

networks:
  urbanx-net:
    driver: bridge
    name: urbanx-staging-net
```

---

## 6.3. Script init nhiều database trong Postgres

Postgres image chỉ tạo 1 DB từ `POSTGRES_DB`. Để tạo 3 DB cho 3 service, dùng init script.

File: `/opt/urbanx/compose/init-multi-db.sh`

```bash
#!/bin/bash
set -e
set -u

if [ -n "${POSTGRES_MULTIPLE_DATABASES:-}" ]; then
    echo "Creating multiple databases: $POSTGRES_MULTIPLE_DATABASES"
    for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
        echo "  → $db"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
            SELECT 'CREATE DATABASE $db'
            WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
EOSQL
    done
fi
```

Cấp quyền thực thi:

```bash
deploy@staging:/opt/urbanx/compose$ chmod +x init-multi-db.sh
```

> Script chỉ chạy lần đầu khi Postgres container init (volume rỗng). Reset DB = xoá volume `postgres_data`.

---

## 6.4. File `.env.staging` (mẫu)

Đặt tại `/opt/urbanx/env/.env.staging` (Jenkins SCP lên — chmod 600):

```ini
# Image registry
DOCKER_HUB_USERNAME=your-dockerhub-username
IMAGE_TAG=develop-latest

# ASP.NET
ASPNETCORE_ENVIRONMENT=Staging

# PostgreSQL
POSTGRES_USER=urbanx_app
POSTGRES_PASSWORD=<openssl-rand-base64-32>
POSTGRES_DB_CATALOG=urbanx_catalog
POSTGRES_DB_IDENTITY=urbanx_identity
POSTGRES_DB_INVENTORY=urbanx_inventory

# RabbitMQ
RABBITMQ_USER=urbanx
RABBITMQ_PASSWORD=<openssl-rand-base64-32>

# Redis
REDIS_PASSWORD=<openssl-rand-base64-32>

# Identity
IDENTITY_ISSUER_URI=https://staging.urbanx.dev
IDENTITY_SIGNING_KEY=<openssl-rand-base64-64>

# OpenTelemetry (optional)
OTEL_EXPORTER_OTLP_ENDPOINT=
```

`IMAGE_TAG` Jenkins sẽ override mỗi lần deploy (truyền qua `-e IMAGE_TAG=...` khi `docker compose up`).

Permission:

```bash
deploy@staging:~$ chmod 600 /opt/urbanx/env/.env.staging
```

---

## 6.5. Naming conventions cho image tag

Đặt tag theo format: `<branch>-<short-sha>` + `<branch>-latest`.

| Tag | Khi nào |
|---|---|
| `develop-a1b2c3d` | Mỗi lần build từ commit `a1b2c3d…` trên `develop` |
| `develop-latest` | Alias trỏ về build mới nhất của `develop` |
| `develop-build-42` | Build number Jenkins (cho rollback rõ ràng) |

Trong pipeline ở bước 7, push cả 3 tag (versioned + latest + build-number).

---

## 6.6. Smoke test compose file local

Trước khi đẩy lên VPS, validate cú pháp:

```bash
deploy@staging:/opt/urbanx/compose$ docker compose --env-file /opt/urbanx/env/.env.staging \
  -f docker-compose.staging.yml config
```

Lệnh `config` render YAML đã merge env — kiểm tra không có biến nào còn dấu `${...}` chưa thay.

Test up:

```bash
deploy@staging:/opt/urbanx/compose$ docker compose --env-file /opt/urbanx/env/.env.staging \
  -f docker-compose.staging.yml up -d
deploy@staging:/opt/urbanx/compose$ docker compose -f docker-compose.staging.yml ps
deploy@staging:/opt/urbanx/compose$ docker compose -f docker-compose.staging.yml logs --tail=50 gateway
```

> Image services sẽ pull fail vì chưa được push lên Docker Hub. Ở bước 7 chạy pipeline đầu xong, quay lại test lại.

---

## 6.7. Mở rộng sau này

Khi enable Order/Payment/Merchant: thêm services tương tự, thêm DB mới vào `POSTGRES_MULTIPLE_DATABASES`, deploy lại.

Khi cần observability: thêm `otel-collector`, `prometheus`, `grafana`, `loki` vào cùng compose file hoặc tách `docker-compose.observability.yml` rồi `docker compose -f a.yml -f b.yml up`.

Xong sang [07-jenkinsfile.md](07-jenkinsfile.md).
