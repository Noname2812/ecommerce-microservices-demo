# Bước 8 — Database Migrations

Mục tiêu: chạy EF Core migrations **trước** khi app start, không cần để app tự `db.Database.Migrate()` lúc startup (anti-pattern trong production multi-replica).

---

## 8.1. Tại sao không `Database.Migrate()` lúc startup

- Race condition khi scale > 1 replica (nhiều container cùng cố apply migration).
- App fail-fast khi DB chưa sẵn sàng → restart loop tốn tài nguyên.
- Hard to rollback: migration đã apply nhưng image mới chưa stable.
- Anti-pattern phổ biến: deploy DB schema và app phải tách rõ.

**Giải pháp**: dùng `dotnet ef migrations bundle` để compile migration thành 1 binary self-contained, chạy như bước build → release artifact riêng.

---

## 8.2. EF Migrations Bundle

Bundle là binary `linux-x64` chứa toàn bộ migration history, chạy như:

```bash
$ ./catalog-efbundle --connection "Host=...;Database=...;Username=...;Password=..."
```

Output:
```
Applying migration '20260101_InitialCreate'...
Applying migration '20260315_AddProductVariant'...
Done.
```

Hoàn toàn idempotent — chạy lại không apply migration đã có.

### Lệnh build bundle

```bash
# Cho mỗi service
$ dotnet ef migrations bundle \
    --self-contained -r linux-x64 \
    --project src/Services/Catalog/UrbanX.Catalog.Persistence \
    --startup-project src/Services/Catalog/UrbanX.Catalog.API \
    --context CatalogDbContext \
    --output ./bundles/catalog-efbundle --force
```

Pipeline đã có stage `Build EF Bundles` ở [07-jenkinsfile.md](07-jenkinsfile.md).

---

## 8.3. Hai cách chạy bundle trên Deploy VPS

### Cách A — Bundle binary chạy thẳng trên host (đã dùng ở Jenkinsfile mẫu)

Pros: đơn giản, không cần build thêm image.
Cons: Postgres container phải expose port ra `127.0.0.1` tạm để bundle kết nối → phải stop/start lại.

### Cách B — Bundle binary đóng vào image migration (recommend)

Pros: chạy được trong cùng Docker network với Postgres (`postgres:5432` thay vì `127.0.0.1:5433`), không cần expose port.
Cons: thêm 1 image (~ 50MB mỗi service).

#### B.1. Dockerfile.migrations

File: `docker/Dockerfile.migrations`

```dockerfile
# syntax=docker/dockerfile:1.7
FROM debian:12-slim
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates \
 && rm -rf /var/lib/apt/lists/*

ARG SERVICE
COPY bundles/${SERVICE}-efbundle /app/efbundle
RUN chmod +x /app/efbundle

ENTRYPOINT ["/app/efbundle"]
```

#### B.2. Thêm stage trong Jenkinsfile (replace `Build EF Bundles` + thêm push)

```groovy
stage('Build Migration Images') {
    steps {
        script {
            ['catalog', 'identity', 'inventory'].each { svc ->
                sh """
                    docker build \
                      --build-arg SERVICE=${svc} \
                      -f docker/Dockerfile.migrations \
                      -t ${DOCKER_HUB_USERNAME}/urbanx-${svc}-migrate:${IMAGE_TAG} \
                      -t ${DOCKER_HUB_USERNAME}/urbanx-${svc}-migrate:${BRANCH_LATEST_TAG} \
                      .
                    docker push ${DOCKER_HUB_USERNAME}/urbanx-${svc}-migrate:${IMAGE_TAG}
                    docker push ${DOCKER_HUB_USERNAME}/urbanx-${svc}-migrate:${BRANCH_LATEST_TAG}
                """
            }
        }
    }
}
```

#### B.3. Compose hoặc one-shot job

Thay stage `Run DB Migrations` đơn giản hơn:

```groovy
stage('Run DB Migrations') {
    when { branch 'develop' }
    steps {
        sshagent(credentials: ['staging-ssh-deploy']) {
            sh '''
                ssh ${DEPLOY_HOST} bash -s <<EOF
set -euo pipefail
cd /opt/urbanx
set -a; source env/.env.staging; set +a

# Đảm bảo postgres đã chạy
docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml up -d postgres
sleep 5

run_migrate() {
  local svc=\$1; local db=\$2
  docker run --rm \
    --network urbanx-staging-net \
    ${DOCKER_HUB_USERNAME}/urbanx-\${svc}-migrate:${IMAGE_TAG} \
    --connection "Host=postgres;Port=5432;Database=\${db};Username=\$POSTGRES_USER;Password=\$POSTGRES_PASSWORD"
}

run_migrate catalog   "\$POSTGRES_DB_CATALOG"
run_migrate identity  "\$POSTGRES_DB_IDENTITY"
run_migrate inventory "\$POSTGRES_DB_INVENTORY"
EOF
            '''
        }
    }
}
```

Khuyến nghị **dùng cách B** cho project học CI/CD chuẩn — sạch sẽ hơn.

---

## 8.4. Add migration mới — workflow cho dev

```bash
# Trên máy local, từ Persistence project
$ cd src/Services/Catalog/UrbanX.Catalog.Persistence
$ dotnet ef migrations add AddProductDimension \
    --startup-project ../UrbanX.Catalog.API \
    --context CatalogDbContext

# Commit migration .cs + Designer.cs + Snapshot
$ git add Migrations/
$ git commit -m "feat(catalog): add product dimension column"
$ git push origin develop
```

Jenkins pipeline tự build bundle mới, apply lên staging.

> Đừng quên include cả file `Migrations/<XX>_AddProductDimension.Designer.cs` và `CatalogDbContextModelSnapshot.cs` — thiếu sẽ làm migration tiếp theo conflict.

---

## 8.5. Rollback migration

EF không hỗ trợ rollback bundle qua flag riêng. Cách rollback:

1. **Generate idempotent SQL script** cho từng migration (luôn build sẵn):
   ```bash
   $ dotnet ef migrations script \
       --idempotent \
       --output catalog-migrations.sql \
       --project ...Persistence --startup-project ...API
   ```
   Pipeline có thể artifact file này → khi cần rollback, sửa thủ công.

2. **Tạo migration revert** mới (recommended cho production):
   ```bash
   $ dotnet ef migrations add Revert_AddProductDimension
   # Edit code: Drop column thay vì AddColumn
   ```
   Deploy bình thường — migration forward-only.

3. **Hard reset** (chỉ staging — DỮ LIỆU MẤT):
   ```bash
   deploy@staging:~$ docker compose -f /opt/urbanx/compose/docker-compose.staging.yml down -v postgres
   ```
   Volume `postgres_data` xoá, init script chạy lại, run lại bundle.

---

## 8.6. Backup DB trước migration (tuỳ chọn nhưng nên có)

Thêm stage trước `Run DB Migrations`:

```groovy
stage('Backup DB') {
    when { branch 'develop' }
    steps {
        sshagent(credentials: ['staging-ssh-deploy']) {
            sh '''
                ssh ${DEPLOY_HOST} bash -s <<'EOF'
set -euo pipefail
cd /opt/urbanx
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
set -a; source env/.env.staging; set +a
mkdir -p backups
for db in "$POSTGRES_DB_CATALOG" "$POSTGRES_DB_IDENTITY" "$POSTGRES_DB_INVENTORY"; do
  docker compose -f compose/docker-compose.staging.yml exec -T postgres \
    pg_dump -U "$POSTGRES_USER" -d "$db" --no-owner --clean --if-exists \
    | gzip > "backups/${db}-${TIMESTAMP}.sql.gz"
done
# Giữ 14 ngày
find backups -name "*.sql.gz" -mtime +14 -delete
EOF
            '''
        }
    }
}
```

Restore:

```bash
deploy@staging:~$ gunzip -c /opt/urbanx/backups/urbanx_catalog-<timestamp>.sql.gz \
  | docker compose -f /opt/urbanx/compose/docker-compose.staging.yml exec -T postgres \
    psql -U urbanx_app -d urbanx_catalog
```

---

## 8.7. Migration cho service mới

Khi enable Order/Payment/Merchant:

1. Tạo Dockerfile cho service (xem [05-dockerfiles.md](05-dockerfiles.md)).
2. Thêm stage build bundle/migration image trong Jenkinsfile.
3. Thêm DB tên mới vào `POSTGRES_MULTIPLE_DATABASES` trong `.env.staging`.
4. Thêm `run_migrate <new-service> "$POSTGRES_DB_<NEW>"` trong stage migration.
5. Thêm service vào `docker-compose.staging.yml`.

---

## 8.8. Lưu ý quan trọng

- **Migration ALTER TABLE lớn** (vd `ALTER COLUMN type`) trên bảng nhiều rows: chạy ngoài giờ + có backup. Bundle không có "online migration" — block writer.
- **Migration thêm `NOT NULL` column**: phải có default hoặc 2-phase (add nullable → backfill → set NOT NULL).
- **Tránh data migration trong EF migration** (`UpdateData`/`InsertData` cho hàng ngàn rows): tách ra job riêng.
- **Không bao giờ chỉnh tay file Migration đã merge** — tạo migration mới revert lại.

Xong sang [09-github-webhook.md](09-github-webhook.md).
