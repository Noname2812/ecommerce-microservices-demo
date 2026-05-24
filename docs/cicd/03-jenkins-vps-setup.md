# Bước 3 — Setup Jenkins VPS

Mục tiêu: Jenkins LTS chạy trong Docker, có sẵn Docker CLI (để build image) và .NET 10 SDK (để build + test solution), truy cập qua HTTPS.

---

## 3.1. Hardening VPS

Áp dụng tương tự bước 2 (mục 2.1 → 2.5): update, tạo user `jenkins-admin` (không phải `jenkins` — tránh đụng user mặc định của Jenkins container), khoá SSH, bật ufw.

```bash
root@ci:~# adduser --gecos "" jenkins-admin && usermod -aG sudo jenkins-admin
# ... copy authorized_keys
```

Firewall:

```bash
root@ci:~# ufw allow 22/tcp
root@ci:~# ufw allow 80/tcp
root@ci:~# ufw allow 443/tcp
root@ci:~# ufw --force enable
```

Không mở 8080 ra ngoài — Jenkins chỉ truy cập qua Nginx + HTTPS.

Cài Docker giống bước 2.6.

---

## 3.2. Custom Jenkins agent image — `urbanx-jenkins-agent`

Jenkins LTS image mặc định chưa có Docker CLI và .NET 10 SDK. Tự build image chứa cả 3.

Tạo `/opt/jenkins/Dockerfile.agent` trên VPS:

```bash
jenkins-admin@ci:~$ sudo mkdir -p /opt/jenkins
jenkins-admin@ci:~$ sudo chown jenkins-admin:jenkins-admin /opt/jenkins
jenkins-admin@ci:~$ nano /opt/jenkins/Dockerfile.agent
```

```dockerfile
# /opt/jenkins/Dockerfile.agent
FROM jenkins/jenkins:lts-jdk21

USER root

# Tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl gnupg lsb-release apt-transport-https \
    git unzip jq wget \
 && rm -rf /var/lib/apt/lists/*

# Docker CLI (không chạy daemon — mount socket từ host)
RUN install -m 0755 -d /etc/apt/keyrings \
 && curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list \
 && apt-get update \
 && apt-get install -y docker-ce-cli docker-buildx-plugin docker-compose-plugin \
 && rm -rf /var/lib/apt/lists/*

# .NET 10 SDK
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --info

# Cài dotnet-ef tool global (cho EF migrations bundle)
ENV PATH="$PATH:/var/jenkins_home/.dotnet/tools"
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef

# Jenkins user cần group docker để dùng socket (matches host docker GID — sẽ set lúc run)
USER jenkins
```

> Lý do `jdk21`: yêu cầu của Jenkins LTS mới nhất.
> Lý do bundle `dotnet-ef`: ở bước 8 sẽ chạy `dotnet ef migrations bundle` ngay trong pipeline.

Build image:

```bash
jenkins-admin@ci:/opt/jenkins$ docker build -t urbanx-jenkins-agent:lts -f Dockerfile.agent .
```

---

## 3.3. Chạy Jenkins container

Tìm GID của group `docker` trên host (để gán cho user jenkins trong container):

```bash
jenkins-admin@ci:~$ getent group docker
docker:x:984:jenkins-admin
                  ^^^ GID là 984 (ví dụ — bạn có thể khác)
```

Run container, mount socket Docker từ host:

```bash
jenkins-admin@ci:~$ docker run -d \
  --name urbanx-jenkins \
  --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -p 127.0.0.1:50000:50000 \
  -v jenkins_home:/var/jenkins_home \
  -v /var/run/docker.sock:/var/run/docker.sock \
  --group-add 984 \
  -e TZ=Asia/Ho_Chi_Minh \
  urbanx-jenkins-agent:lts
```

Giải thích flag:
- `-p 127.0.0.1:8080:8080` — chỉ bind localhost, Nginx sẽ proxy.
- `-v jenkins_home:/var/jenkins_home` — volume named, KHÔNG mount thẳng host path để tránh permission UID nhức đầu.
- `-v /var/run/docker.sock:/var/run/docker.sock` — cho phép pipeline build/push image.
- `--group-add 984` — gán user jenkins vào group có quyền truy cập socket.

Lấy initial admin password:

```bash
jenkins-admin@ci:~$ docker exec urbanx-jenkins cat /var/jenkins_home/secrets/initialAdminPassword
```

---

## 3.4. Nginx + HTTPS cho Jenkins

`/etc/nginx/sites-available/jenkins`:

```nginx
upstream jenkins_backend {
    server 127.0.0.1:8080;
}

server {
    listen 80;
    server_name ci.urbanx.dev;

    # Cho phép payload webhook lớn
    client_max_body_size 50m;

    # Jenkins yêu cầu header này
    ignore_invalid_headers off;

    location / {
        proxy_pass http://jenkins_backend;
        proxy_http_version 1.1;
        proxy_redirect off;

        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Long-polling (Jenkins UI)
        proxy_buffering off;
        proxy_request_buffering off;
        proxy_read_timeout 600s;
    }
}
```

```bash
root@ci:~# ln -sf /etc/nginx/sites-available/jenkins /etc/nginx/sites-enabled/jenkins
root@ci:~# nginx -t && systemctl reload nginx
root@ci:~# certbot --nginx -d ci.urbanx.dev --redirect --agree-tos -m admin@urbanx.dev --non-interactive
```

---

## 3.5. Cấu hình ban đầu Jenkins UI

Vào `https://ci.urbanx.dev`:

1. Paste initial admin password.
2. **"Install suggested plugins"** — chấp nhận default (Git, Pipeline, Folder, Credentials Binding…).
3. Tạo admin user (KHÔNG dùng default `admin`).
4. **Manage Jenkins → System → Jenkins URL**: đặt `https://ci.urbanx.dev/`.
5. **Manage Jenkins → Security → Configure Global Security**:
   - Authentication: Jenkins' own user database (đã setup).
   - Authorization: Logged-in users can do anything (cho learning — production nên Matrix-based).
   - CSRF: bật default.

---

## 3.6. Cài thêm plugins cần thiết

**Manage Jenkins → Plugins → Available**, tìm và install:

| Plugin | Mục đích |
|---|---|
| Docker Pipeline | DSL `docker.build`, `docker.push` |
| SSH Agent | `sshagent` step để SSH lên Deploy VPS |
| Pipeline Utility Steps | `readJSON`, `writeFile`, `findFiles`… |
| Credentials Binding | `withCredentials` step |
| GitHub Integration | Webhook trigger |
| Blue Ocean | UI pipeline đẹp (optional) |
| AnsiColor | Render ANSI color trong console |
| Timestamper | Timestamp trên mỗi log line |
| Slack Notification | (optional) ping team khi deploy |

Restart Jenkins sau khi cài.

---

## 3.7. Kiểm tra cuối bước

Trong Jenkins UI, tạo job "Freestyle project" → Build step "Execute shell":

```bash
echo "=== Tools versions ==="
dotnet --version
docker version --format '{{.Client.Version}}'
git --version
dotnet ef --version
```

Run job — output phải show .NET 10.x, Docker 24+, dotnet-ef bundle.

Xong sang [04-jenkins-credentials.md](04-jenkins-credentials.md).
