# Bước 5 — Dockerfile cho từng service

Mục tiêu: mỗi service có 1 Dockerfile multi-stage tối ưu cache, sản xuất image runtime nhẹ, an toàn (non-root user).

> Hiện tại repo đã có Dockerfile cho Gateway và Catalog. Catalog Dockerfile đang outdated (tham chiếu các project `Shared.EventBus/Kafka/...` đã bị xoá). Cần update + tạo mới cho Identity và Inventory.

---

## 5.1. Quy ước chung

- Base build: `mcr.microsoft.com/dotnet/sdk:10.0`
- Base runtime: `mcr.microsoft.com/dotnet/aspnet:10.0`
- Multi-stage: `build` → `final`
- Layer cache: copy `Directory.Packages.props` + `nuget.config` + `.csproj` trước, restore, sau đó mới copy toàn bộ source
- Run-as non-root: user `urbanx` (UID 1001)
- Expose `8080` (port HTTP standard của ASP.NET Core)
- Healthcheck: `curl http://localhost:8080/alive` — endpoint do ServiceDefaults expose

`.dockerignore` ở root repo (đã có sẵn) — kiểm tra bao gồm:

```
.git
.vs
.idea
.vscode
tests/
**/bin/
**/obj/
**/TestResults/
*.md
*.sh
*.ps1
*.http
Dockerfile*
docker-compose*.yml
.dockerignore
src/AppHost/
.nuget/
```

---

## 5.2. Dockerfile — Gateway

File: `src/Gateway/Dockerfile` (cập nhật từ bản hiện tại).

```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Central package management + restore inputs
COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["UrbanX.sln", "./"]

# Csproj của Gateway và transitive references
COPY ["src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj", "src/ServiceDefaults/UrbanX.ServiceDefaults/"]
COPY ["src/Shared/Shared.Kernel/Shared.Kernel.csproj", "src/Shared/Shared.Kernel/"]
COPY ["src/Shared/Shared.Cache/Shared.Cache.csproj", "src/Shared/Shared.Cache/"]
COPY ["src/Gateway/UrbanX.Gateway/UrbanX.Gateway.csproj", "src/Gateway/UrbanX.Gateway/"]
COPY ["src/Gateway/UrbanX.Gateway.Application/UrbanX.Gateway.Application.csproj", "src/Gateway/UrbanX.Gateway.Application/"]
COPY ["src/Gateway/UrbanX.Gateway.Infrastructure/UrbanX.Gateway.Infrastructure.csproj", "src/Gateway/UrbanX.Gateway.Infrastructure/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Gateway/UrbanX.Gateway/UrbanX.Gateway.csproj"

COPY . .
WORKDIR /src/src/Gateway/UrbanX.Gateway
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "UrbanX.Gateway.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

RUN apt-get update && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

RUN groupadd -r -g 1001 urbanx && useradd -r -u 1001 -g urbanx urbanx

COPY --from=build --chown=urbanx:urbanx /app/publish .

USER 1001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "UrbanX.Gateway.dll"]
```

> `--mount=type=cache,target=/root/.nuget/packages` chỉ hoạt động khi Docker BuildKit bật (mặc định ở Docker 23+). Giúp restore lần 2 cực nhanh.

---

## 5.3. Dockerfile — Catalog

File: `src/Services/Catalog/Dockerfile` (cập nhật bản cũ).

```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["UrbanX.sln", "./"]

# Shared
COPY ["src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj", "src/ServiceDefaults/UrbanX.ServiceDefaults/"]
COPY ["src/Shared/Shared.Kernel/Shared.Kernel.csproj", "src/Shared/Shared.Kernel/"]
COPY ["src/Shared/Shared.Cache/Shared.Cache.csproj", "src/Shared/Shared.Cache/"]
COPY ["src/Shared/Shared.Contract/Shared.Contract.csproj", "src/Shared/Shared.Contract/"]
COPY ["src/Shared/Shared.Application/Shared.Application.csproj", "src/Shared/Shared.Application/"]
COPY ["src/Shared/Shared.Messaging/Shared.Messaging.csproj", "src/Shared/Shared.Messaging/"]
COPY ["src/Shared/Shared.Observability/Shared.Observability.csproj", "src/Shared/Shared.Observability/"]

# Catalog
COPY ["src/Services/Catalog/UrbanX.Catalog.Domain/UrbanX.Catalog.Domain.csproj", "src/Services/Catalog/UrbanX.Catalog.Domain/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.Application/UrbanX.Catalog.Application.csproj", "src/Services/Catalog/UrbanX.Catalog.Application/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.Persistence/UrbanX.Catalog.Persistence.csproj", "src/Services/Catalog/UrbanX.Catalog.Persistence/"]
COPY ["src/Services/Catalog/UrbanX.Catalog.API/UrbanX.Catalog.API.csproj", "src/Services/Catalog/UrbanX.Catalog.API/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Services/Catalog/UrbanX.Catalog.API/UrbanX.Catalog.API.csproj"

COPY . .
WORKDIR /src/src/Services/Catalog/UrbanX.Catalog.API
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "UrbanX.Catalog.API.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
RUN groupadd -r -g 1001 urbanx && useradd -r -u 1001 -g urbanx urbanx

COPY --from=build --chown=urbanx:urbanx /app/publish .

USER 1001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "UrbanX.Catalog.API.dll"]
```

> Kiểm tra lại tên project `Shared.*` chính xác với file `.csproj` thật trong repo. Nếu khác, sửa path tương ứng. Đây là điểm dễ sai nhất khi viết Dockerfile multi-stage.

---

## 5.4. Dockerfile — Identity

File: `src/Services/Identity/Dockerfile` (tạo mới).

```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["UrbanX.sln", "./"]

COPY ["src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj", "src/ServiceDefaults/UrbanX.ServiceDefaults/"]
COPY ["src/Shared/Shared.Kernel/Shared.Kernel.csproj", "src/Shared/Shared.Kernel/"]
COPY ["src/Shared/Shared.Cache/Shared.Cache.csproj", "src/Shared/Shared.Cache/"]
COPY ["src/Shared/Shared.Contract/Shared.Contract.csproj", "src/Shared/Shared.Contract/"]
COPY ["src/Shared/Shared.Application/Shared.Application.csproj", "src/Shared/Shared.Application/"]
COPY ["src/Shared/Shared.Messaging/Shared.Messaging.csproj", "src/Shared/Shared.Messaging/"]
COPY ["src/Shared/Shared.Observability/Shared.Observability.csproj", "src/Shared/Shared.Observability/"]

COPY ["src/Services/Identity/UrbanX.Identity.Domain/UrbanX.Identity.Domain.csproj", "src/Services/Identity/UrbanX.Identity.Domain/"]
COPY ["src/Services/Identity/UrbanX.Identity.Application/UrbanX.Identity.Application.csproj", "src/Services/Identity/UrbanX.Identity.Application/"]
COPY ["src/Services/Identity/UrbanX.Identity.Infrastructure/UrbanX.Identity.Infrastructure.csproj", "src/Services/Identity/UrbanX.Identity.Infrastructure/"]
COPY ["src/Services/Identity/UrbanX.Identity.Persistence/UrbanX.Identity.Persistence.csproj", "src/Services/Identity/UrbanX.Identity.Persistence/"]
COPY ["src/Services/Identity/UrbanX.Identity.API/UrbanX.Identity.API.csproj", "src/Services/Identity/UrbanX.Identity.API/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Services/Identity/UrbanX.Identity.API/UrbanX.Identity.API.csproj"

COPY . .
WORKDIR /src/src/Services/Identity/UrbanX.Identity.API
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "UrbanX.Identity.API.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
RUN groupadd -r -g 1001 urbanx && useradd -r -u 1001 -g urbanx urbanx

COPY --from=build --chown=urbanx:urbanx /app/publish .

USER 1001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "UrbanX.Identity.API.dll"]
```

---

## 5.5. Dockerfile — Inventory

File: `src/Services/Inventory/Dockerfile` (tạo mới).

```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["UrbanX.sln", "./"]

COPY ["src/ServiceDefaults/UrbanX.ServiceDefaults/UrbanX.ServiceDefaults.csproj", "src/ServiceDefaults/UrbanX.ServiceDefaults/"]
COPY ["src/Shared/Shared.Kernel/Shared.Kernel.csproj", "src/Shared/Shared.Kernel/"]
COPY ["src/Shared/Shared.Cache/Shared.Cache.csproj", "src/Shared/Shared.Cache/"]
COPY ["src/Shared/Shared.Contract/Shared.Contract.csproj", "src/Shared/Shared.Contract/"]
COPY ["src/Shared/Shared.Application/Shared.Application.csproj", "src/Shared/Shared.Application/"]
COPY ["src/Shared/Shared.Messaging/Shared.Messaging.csproj", "src/Shared/Shared.Messaging/"]
COPY ["src/Shared/Shared.Observability/Shared.Observability.csproj", "src/Shared/Shared.Observability/"]

COPY ["src/Services/Inventory/UrbanX.Inventory.Domain/UrbanX.Inventory.Domain.csproj", "src/Services/Inventory/UrbanX.Inventory.Domain/"]
COPY ["src/Services/Inventory/UrbanX.Inventory.Application/UrbanX.Inventory.Application.csproj", "src/Services/Inventory/UrbanX.Inventory.Application/"]
COPY ["src/Services/Inventory/UrbanX.Inventory.Infrastructure/UrbanX.Inventory.Infrastructure.csproj", "src/Services/Inventory/UrbanX.Inventory.Infrastructure/"]
COPY ["src/Services/Inventory/UrbanX.Inventory.Persistence/UrbanX.Inventory.Persistence.csproj", "src/Services/Inventory/UrbanX.Inventory.Persistence/"]
COPY ["src/Services/Inventory/UrbanX.Inventory.API/UrbanX.Inventory.API.csproj", "src/Services/Inventory/UrbanX.Inventory.API/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Services/Inventory/UrbanX.Inventory.API/UrbanX.Inventory.API.csproj"

COPY . .
WORKDIR /src/src/Services/Inventory/UrbanX.Inventory.API
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "UrbanX.Inventory.API.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
RUN groupadd -r -g 1001 urbanx && useradd -r -u 1001 -g urbanx urbanx

COPY --from=build --chown=urbanx:urbanx /app/publish .

USER 1001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "UrbanX.Inventory.API.dll"]
```

---

## 5.6. Build context — root repo

Tất cả Dockerfile dùng path tương đối từ root. Khi build:

```bash
$ docker build -f src/Gateway/Dockerfile -t urbanx-gateway:dev .
$ docker build -f src/Services/Catalog/Dockerfile -t urbanx-catalog:dev .
$ docker build -f src/Services/Identity/Dockerfile -t urbanx-identity:dev .
$ docker build -f src/Services/Inventory/Dockerfile -t urbanx-inventory:dev .
```

Lệnh build chạy ở **root repo** (`UrbanX.sln`), KHÔNG chạy trong từng folder service. Đây là điểm khác biệt quan trọng: multi-stage cần truy cập `Directory.Packages.props`, `nuget.config`, `UrbanX.sln` ở root.

---

## 5.7. Tối ưu kích thước image

Tham khảo:
- `mcr.microsoft.com/dotnet/aspnet:10.0` ~ 220 MB
- Sau khi publish app: thêm ~ 30–50 MB → final image ~ 250–280 MB.

Muốn nhẹ hơn nữa, dùng `aspnet:10.0-alpine`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN apk add --no-cache curl icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
```

Image còn ~ 100 MB. Trade-off: musl libc đôi khi gây issue khi p/invoke native lib (Stripe, Npgsql native…) — test kỹ trước.

---

## 5.8. Test local trước khi commit

```bash
$ cd <repo-root>
$ docker build -f src/Services/Catalog/Dockerfile -t urbanx-catalog:test .
$ docker run --rm -p 8080:8080 \
    -e ASPNETCORE_ENVIRONMENT=Development \
    -e "ConnectionStrings__catalogdb=Host=host.docker.internal;Database=urbanx_catalog;Username=postgres;Password=postgres" \
    urbanx-catalog:test
```

Check `http://localhost:8080/alive` trả `Healthy`.

---

## 5.9. Lưu ý

- Khi thêm `ProjectReference` mới vào service, **bắt buộc** thêm dòng `COPY ["..."]` tương ứng — quên là restore thất bại trong build.
- Đổi tên project hoặc namespace → update Dockerfile cùng commit.
- Nếu khó maintain, có thể thay phần `COPY *.csproj` từng dòng bằng script COPY toàn bộ — nhưng cache layer sẽ kém hơn (mỗi lần đổi file `.cs` cũng buộc restore lại).

Xong sang [06-docker-compose-staging.md](06-docker-compose-staging.md).
