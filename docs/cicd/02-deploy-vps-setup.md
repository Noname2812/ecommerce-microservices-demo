# Bước 2 — Setup Deploy VPS (Staging)

Mục tiêu: VPS Ubuntu 22.04 sẵn sàng chạy `docker compose` với user `deploy` không root, firewall an toàn, swap đủ.

> Chạy lệnh dưới đây bằng SSH với user `root` (hoặc user khởi tạo có sudo).

---

## 2.1. Cập nhật hệ thống

```bash
root@staging:~# apt update && apt upgrade -y
root@staging:~# apt install -y curl wget ufw fail2ban ca-certificates gnupg lsb-release unattended-upgrades
root@staging:~# dpkg-reconfigure -plow unattended-upgrades   # bật auto-update bảo mật
```

Đặt timezone:

```bash
root@staging:~# timedatectl set-timezone Asia/Ho_Chi_Minh
```

---

## 2.2. Tạo user `deploy`

```bash
root@staging:~# adduser --gecos "" deploy
root@staging:~# usermod -aG sudo deploy
root@staging:~# mkdir -p /home/deploy/.ssh
root@staging:~# cp ~/.ssh/authorized_keys /home/deploy/.ssh/   # nếu đã ssh root bằng key
root@staging:~# chown -R deploy:deploy /home/deploy/.ssh
root@staging:~# chmod 700 /home/deploy/.ssh
root@staging:~# chmod 600 /home/deploy/.ssh/authorized_keys
```

Thêm public key Jenkins sẽ dùng (sẽ lấy từ bước 4 — giờ tạo placeholder):

```bash
root@staging:~# echo "<paste-noi-dung-urbanx_jenkins_deploy.pub-o-day>" >> /home/deploy/.ssh/authorized_keys
```

Cấu hình sudo không cần password (chỉ cho command docker compose, an toàn hơn full NOPASSWD):

```bash
root@staging:~# tee /etc/sudoers.d/deploy-docker <<EOF
deploy ALL=(ALL) NOPASSWD: /usr/bin/docker, /usr/local/bin/docker-compose, /usr/bin/systemctl restart nginx
EOF
root@staging:~# chmod 440 /etc/sudoers.d/deploy-docker
```

---

## 2.3. Khoá SSH — chỉ key, không password root

Sửa `/etc/ssh/sshd_config`:

```ini
PermitRootLogin no
PasswordAuthentication no
PubkeyAuthentication yes
```

```bash
root@staging:~# systemctl reload ssh
```

> ⚠️ Mở terminal mới SSH vào user `deploy` để test trước khi đóng session root. Nếu lock chính mình ra, dùng VPS console của provider.

---

## 2.4. Firewall (ufw)

```bash
root@staging:~# ufw default deny incoming
root@staging:~# ufw default allow outgoing
root@staging:~# ufw allow 22/tcp           # SSH
root@staging:~# ufw allow 80/tcp           # HTTP (Nginx)
root@staging:~# ufw allow 443/tcp          # HTTPS (Nginx)
root@staging:~# ufw --force enable
root@staging:~# ufw status verbose
```

Lưu ý: **không** mở 5432 (Postgres), 5672 (RabbitMQ), 6379 (Redis), 5025/5005/5020 (services) ra ngoài. Tất cả gọi qua Nginx → Gateway hoặc trong Docker network.

---

## 2.5. Swap (nếu RAM 8GB không có swap)

```bash
root@staging:~# fallocate -l 4G /swapfile
root@staging:~# chmod 600 /swapfile
root@staging:~# mkswap /swapfile
root@staging:~# swapon /swapfile
root@staging:~# echo '/swapfile none swap sw 0 0' >> /etc/fstab
root@staging:~# sysctl vm.swappiness=10
root@staging:~# echo 'vm.swappiness=10' >> /etc/sysctl.conf
```

---

## 2.6. Cài Docker Engine + Compose plugin

Theo hướng dẫn chính thức Docker:

```bash
root@staging:~# install -m 0755 -d /etc/apt/keyrings
root@staging:~# curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
root@staging:~# chmod a+r /etc/apt/keyrings/docker.gpg
root@staging:~# echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  > /etc/apt/sources.list.d/docker.list
root@staging:~# apt update
root@staging:~# apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Cho user `deploy` chạy docker không cần sudo:

```bash
root@staging:~# usermod -aG docker deploy
```

Logout SSH, login lại user `deploy`, test:

```bash
deploy@staging:~$ docker version
deploy@staging:~$ docker compose version
deploy@staging:~$ docker run --rm hello-world
```

---

## 2.7. Cấu hình Docker daemon

Tạo `/etc/docker/daemon.json`:

```json
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "20m",
    "max-file": "5"
  },
  "live-restore": true,
  "default-address-pools": [
    { "base": "172.30.0.0/16", "size": 24 }
  ]
}
```

```bash
root@staging:~# systemctl restart docker
```

Lý do: log-rotation (tránh `/var/lib/docker/containers/*-json.log` chiếm disk), `live-restore` giúp service không bị kill khi restart Docker daemon.

---

## 2.8. Tạo project directory

```bash
deploy@staging:~$ sudo mkdir -p /opt/urbanx
deploy@staging:~$ sudo chown deploy:deploy /opt/urbanx
deploy@staging:~$ mkdir -p /opt/urbanx/{compose,env,bundles,backups,nginx}
```

Layout sau khi xong sẽ là:

```
/opt/urbanx/
├── compose/                  ← chứa docker-compose.staging.yml
├── env/                      ← .env.staging (chmod 600, do Jenkins copy lên)
├── bundles/                  ← efbundle binary cho EF migrations
├── backups/                  ← postgres dump khi rollback DB
└── nginx/                    ← nginx.conf + cert (Let's Encrypt)
```

---

## 2.9. Cài Nginx + Certbot (reverse proxy + TLS)

```bash
root@staging:~# apt install -y nginx certbot python3-certbot-nginx
```

Cấu hình tối thiểu `/etc/nginx/sites-available/urbanx-staging`:

```nginx
server {
    listen 80;
    server_name staging.urbanx.dev;

    client_max_body_size 20m;

    location / {
        proxy_pass http://127.0.0.1:5000;   # cổng Gateway expose ra host
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}
```

```bash
root@staging:~# ln -sf /etc/nginx/sites-available/urbanx-staging /etc/nginx/sites-enabled/urbanx-staging
root@staging:~# rm -f /etc/nginx/sites-enabled/default
root@staging:~# nginx -t && systemctl reload nginx
```

Sau khi DNS trỏ về VPS, bật HTTPS:

```bash
root@staging:~# certbot --nginx -d staging.urbanx.dev --redirect --agree-tos -m admin@urbanx.dev --non-interactive
```

Cert auto-renew bằng systemd timer `certbot.timer` (sẵn có).

---

## 2.10. Kiểm tra cuối bước

| Check | Lệnh | Kỳ vọng |
|---|---|---|
| Docker chạy | `docker info` | Không lỗi |
| Compose | `docker compose version` | v2.x trở lên |
| Firewall | `sudo ufw status` | 22/80/443 active |
| Nginx | `curl -I http://staging.urbanx.dev` | 502 (chưa có service phía sau — đúng) |
| Disk | `df -h /` | Còn ≥ 30GB |

Tick xong sang [03-jenkins-vps-setup.md](03-jenkins-vps-setup.md).
