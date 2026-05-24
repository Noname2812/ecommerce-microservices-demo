# Bước 10 — Rollback, Logs, Monitoring & Troubleshooting

Mục tiêu: biết cách rollback nhanh khi deploy lỗi, đọc logs, kiểm tra health, và xử lý các tình huống thường gặp.

---

## 10.1. Rollback chiến lược

Mỗi build push image với 3 tag:
- `develop-<build-number>-<sha>` (versioned, **không bao giờ overwrite**)
- `develop-latest` (rolling)
- `develop-build-<N>` (optional alias build number)

→ Rollback = trỏ `IMAGE_TAG` về phiên bản trước, `docker compose pull` + `up -d`.

### Cách 1 — Manual rollback (5 phút)

SSH vào Deploy VPS:

```bash
deploy@staging:~$ cd /opt/urbanx
deploy@staging:/opt/urbanx$ docker images | grep urbanx | head -20
```

Xác định tag versioned của build muốn revert (ví dụ `develop-41-9abcdef`).

```bash
deploy@staging:/opt/urbanx$ sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=develop-41-9abcdef/' env/.env.staging
deploy@staging:/opt/urbanx$ docker compose --env-file env/.env.staging \
    -f compose/docker-compose.staging.yml pull
deploy@staging:/opt/urbanx$ docker compose --env-file env/.env.staging \
    -f compose/docker-compose.staging.yml up -d
```

> **Cẩn thận với DB migration**: rollback image app không tự rollback schema. Nếu build mới chứa migration breaking → xem [08-database-migrations.md §8.5](08-database-migrations.md).

### Cách 2 — Jenkins rollback job

Tạo Jenkins job `urbanx-rollback` (Pipeline với parameter):

```groovy
pipeline {
    agent any
    parameters {
        string(name: 'ROLLBACK_TAG', defaultValue: 'develop-latest', description: 'Image tag để rollback về')
    }
    environment {
        DEPLOY_HOST = 'deploy@<deploy-vps-ip>'
    }
    stages {
        stage('Rollback') {
            steps {
                sshagent(credentials: ['staging-ssh-deploy']) {
                    sh '''
                        ssh ${DEPLOY_HOST} bash -s <<EOF
set -euo pipefail
cd /opt/urbanx
sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG='"${ROLLBACK_TAG}"'/' env/.env.staging
docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml pull
docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml up -d
EOF
                    '''
                }
            }
        }
    }
}
```

Chạy job → nhập tag → done.

---

## 10.2. Truy cập logs

```bash
# Logs realtime, kèm tên service
deploy@staging:/opt/urbanx$ docker compose -f compose/docker-compose.staging.yml logs -f --tail=100

# Chỉ 1 service
deploy@staging:/opt/urbanx$ docker compose -f compose/docker-compose.staging.yml logs -f --tail=200 catalog

# Filter lỗi
deploy@staging:/opt/urbanx$ docker compose -f compose/docker-compose.staging.yml logs --tail=500 catalog \
  | grep -iE "error|exception|fail"
```

Logs đã được rotate (`max-size: 20m`, `max-file: 5`) → mỗi service giữ tối đa 100MB log.

Vị trí raw log files (nếu cần grep nhanh):

```bash
deploy@staging:~$ sudo ls -lh /var/lib/docker/containers/*/{*-json.log,*.log}
```

---

## 10.3. Health checks

Mỗi service expose 2 endpoint từ `ServiceDefaults`:
- `/alive` — liveness probe (process còn chạy).
- `/health` — readiness probe (kèm DB/RabbitMQ/Redis health).

```bash
# Trên Deploy VPS (gateway expose 5000)
deploy@staging:~$ curl -s http://127.0.0.1:5000/alive
Healthy

deploy@staging:~$ curl -s http://127.0.0.1:5000/health | jq
```

Trong container nội bộ:

```bash
deploy@staging:~$ docker compose -f /opt/urbanx/compose/docker-compose.staging.yml exec catalog \
    curl -s http://localhost:8080/health
```

Docker healthcheck status:

```bash
deploy@staging:~$ docker compose -f /opt/urbanx/compose/docker-compose.staging.yml ps
NAME                 STATUS                    PORTS
urbanx-catalog       Up 5 minutes (healthy)
urbanx-gateway       Up 5 minutes (healthy)    127.0.0.1:5000->8080/tcp
urbanx-identity      Up 5 minutes (healthy)
urbanx-inventory     Up 5 minutes (healthy)
urbanx-postgres      Up 5 minutes (healthy)
urbanx-rabbitmq      Up 5 minutes (healthy)
urbanx-redis         Up 5 minutes (healthy)
```

`(unhealthy)` = healthcheck fail 3 lần → restart container.

---

## 10.4. Monitoring tối thiểu (không cần Prometheus)

### Uptime check từ ngoài

Dùng dịch vụ free: **UptimeRobot**, **Better Stack**, **Hyperping**.

| Endpoint | Interval |
|---|---|
| `https://ci.urbanx.dev/login` | 5 min |
| `https://staging.urbanx.dev/alive` | 5 min |

→ Email/SMS alert khi down.

### Disk + Memory trên VPS

Thêm cron job báo cáo qua webhook Discord/Slack:

```bash
deploy@staging:~$ crontab -e
```

```cron
*/15 * * * * df -h / | awk 'NR==2 && int($5) > 85 {print "DISK WARN: "$5" used"}'
*/15 * * * * free -m | awk 'NR==2 && ($3/$2*100) > 90 {printf "MEM WARN: %.0f%% used\n", $3/$2*100}'
```

(Gửi vào webhook bằng `curl -X POST ... | discord-webhook` nếu muốn).

### Sau này — full observability stack

Thêm vào `docker-compose.staging.yml` các service:
- `otel-collector` (gateway OTLP)
- `prometheus` + `grafana` (metrics)
- `loki` + `promtail` (logs)
- `tempo` hoặc `jaeger` (traces)

ServiceDefaults đã wire OpenTelemetry — chỉ cần set `OTEL_EXPORTER_OTLP_ENDPOINT` trong `.env.staging` trỏ về otel-collector container.

---

## 10.5. Troubleshooting checklist

### Pipeline fail ở stage Build

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `dotnet restore` lỗi `NU1605 package downgrade` | Drift trong `Directory.Packages.props` | Check `dotnet list package --outdated` |
| `dotnet build` lỗi `CS0246 missing namespace` | Project mới chưa add reference | Sửa csproj, push lại |
| `dotnet test` fail nhưng local pass | Test phụ thuộc Postgres/Redis | Mock hoặc tag `[Skip(reason)]` trong CI |

### Pipeline fail ở stage Docker Build

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `COPY failed: file not found` | Csproj name đổi nhưng Dockerfile chưa update | Sync Dockerfile + repo |
| `BuildKit not enabled` | Docker < 23 | Update Docker hoặc set `DOCKER_BUILDKIT=1` |
| Slow build > 10 min | Layer cache chết | Mount BuildKit cache hoặc dùng `docker buildx` |

### Pipeline fail ở stage Push

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `unauthorized: authentication required` | Docker Hub token sai/hết hạn | Tạo token mới, update credential |
| `denied: requested access to the resource is denied` | Tag sai prefix repo | Đảm bảo `${DOCKER_HUB_USERNAME}/urbanx-xxx` khớp owner |
| Rate limit | Quá nhiều build trong giờ | Login authenticated (đã làm) hoặc upgrade plan |

### Pipeline fail ở stage Deploy

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `Permission denied (publickey)` | SSH key chưa add hoặc sai | Re-add public key vào `/home/deploy/.ssh/authorized_keys` |
| `Host key verification failed` | known_hosts thiếu | `ssh-keyscan` lại |
| Compose pull `manifest unknown` | Tag chưa được push | Check stage Push ở Jenkins console |
| Container `exited (139)` | OOM | Tăng RAM VPS hoặc set `mem_limit` trong compose |

### App crash loop

```bash
deploy@staging:~$ docker compose -f /opt/urbanx/compose/docker-compose.staging.yml logs --tail=200 catalog
```

Tìm exception đầu tiên — thường:
- Connection string sai (host `postgres` chứ không phải `localhost`).
- Migration chưa apply → table thiếu.
- Env var thiếu (kiểm tra `.env.staging` có biến nào còn rỗng).
- Identity service chưa healthy → service khác fail validate JWT (nhưng trust-gateway pattern thì services không tự verify JWT — check lại config).

---

## 10.6. Cleanup định kỳ

Cron job hàng tuần trên Deploy VPS:

```bash
deploy@staging:~$ crontab -e
```

```cron
# Sunday 03:00 — clean Docker
0 3 * * 0 docker image prune -af --filter "until=168h" >> /var/log/docker-prune.log 2>&1
0 3 * * 0 docker container prune -f >> /var/log/docker-prune.log 2>&1
0 3 * * 0 docker volume prune -f --filter label!=keep >> /var/log/docker-prune.log 2>&1

# Daily 02:00 — backup DB
0 2 * * * /opt/urbanx/scripts/backup-db.sh
```

> Volume `postgres_data`/`rabbitmq_data`/`redis_data` KHÔNG có label `keep` mặc định. Để tránh prune nhầm, đặt label trong compose:
> ```yaml
> volumes:
>   postgres_data:
>     labels:
>       keep: "true"
> ```

---

## 10.7. Quy trình incident response

1. **Quan sát**: alert đến (uptime check, log error spike).
2. **Triage**: `docker compose ps` → service nào unhealthy?
3. **Logs**: `docker compose logs --tail=200 <service>` → tìm root cause.
4. **Decide**:
   - Lỗi nhỏ → fix forward (commit fix, deploy mới).
   - Lỗi nghiêm trọng → rollback ngay (xem 10.1).
5. **Post-mortem**: ghi `docs/incidents/<date>-<title>.md` — gì xảy ra, tại sao, học được gì.

---

## 10.8. Cải tiến tiếp theo

- **Blue/Green deploy**: chạy 2 stack `staging-blue` + `staging-green`, nginx switch upstream.
- **Canary**: route 10% traffic vào image mới qua YARP weight.
- **Auto-rollback**: pipeline thêm stage health-check sau 5 phút deploy, fail → trigger rollback job.
- **Production**: copy bộ docs này → `docs/cicd-prod/`, thêm VPS prod + branch `main` + manual approval.
- **GitOps**: thay SSH deploy bằng ArgoCD/Flux nếu lên Kubernetes.

---

Done. Đọc xong cả 10 bước, anh có pipeline CI/CD chạy được end-to-end. Có vướng bước nào hỏi lại em.
