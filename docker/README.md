# Docker infrastructure (local)

## RabbitMQ

Chạy Postgres + RabbitMQ:

```bash
docker compose up -d
```

Tùy chọn credentials (mặc định `guest` / `guest`):

```bash
set RABBITMQ_USER=myuser
set RABBITMQ_PASS=mypass
docker compose up -d rabbitmq
```

- **AMQP:** `localhost:5672`
- **Management UI:** http://localhost:15672
- **Data:** volume Docker `rabbitmq_data` — broker state (queues/messages) giữ qua `docker compose down` (không kèm `-v`).

**Topology** do **MassTransit** quản lý tại runtime (không import `definitions.json` trong compose — tránh xung đột tên queue với `SetKebabCaseEndpointNameFormatter()`). Xem `docker/rabbitmq/README.md`.

Khi dùng **.NET Aspire** (`UrbanX.AppHost`), broker vẫn cấp qua AppHost; compose phục vụ môi trường **chỉ docker** hoặc tham chiếu local.

## PostgreSQL

Port `5432`, user/password `postgres` / `postgres` — xem `docker-compose.yml`.
