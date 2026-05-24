# Bước 4 — Jenkins Credentials

Mục tiêu: nạp đủ secret cho pipeline — Docker Hub token, SSH key deploy, env file staging, GitHub PAT.

> Tất cả credential lưu domain **Global** scope: `Manage Jenkins → Credentials → System → Global credentials → Add Credentials`.

---

## 4.1. Docker Hub access token

Vào [hub.docker.com](https://hub.docker.com) → Account Settings → Security → **New Access Token**.

- Description: `urbanx-jenkins-ci`
- Permissions: **Read, Write, Delete**
- Sao chép token (chỉ hiện 1 lần).

Trong Jenkins:

| Field | Giá trị |
|---|---|
| Kind | Username with password |
| ID | `docker-hub-creds` |
| Username | `<dockerhub-username>` |
| Password | `<token vừa tạo>` |
| Description | Docker Hub push token |

> Trong Jenkinsfile dùng: `environment { DOCKER_HUB = credentials('docker-hub-creds') }` → tự inject `DOCKER_HUB_USR` + `DOCKER_HUB_PSW`.

---

## 4.2. SSH key Jenkins → Deploy VPS

### Bước 4.2.1 — Add public key vào Deploy VPS

Trên máy local (đã sinh ở bước 1):

```bash
$ cat ~/.ssh/urbanx_jenkins_deploy.pub
```

SSH vào Deploy VPS, append vào user `deploy`:

```bash
deploy@staging:~$ echo "<paste-public-key-day>" >> ~/.ssh/authorized_keys
deploy@staging:~$ chmod 600 ~/.ssh/authorized_keys
```

Test từ máy local:

```bash
$ ssh -i ~/.ssh/urbanx_jenkins_deploy deploy@<deploy-vps-ip>
deploy@staging:~$ exit
```

### Bước 4.2.2 — Upload private key vào Jenkins

| Field | Giá trị |
|---|---|
| Kind | SSH Username with private key |
| ID | `staging-ssh-deploy` |
| Username | `deploy` |
| Private Key | Enter directly → paste nội dung file `urbanx_jenkins_deploy` (private) |
| Passphrase | (để trống nếu không set) |

### Bước 4.2.3 — Khai báo known_hosts

Lấy fingerprint host của Deploy VPS:

```bash
jenkins-admin@ci:~$ ssh-keyscan -t ed25519 <deploy-vps-ip>
```

Copy dòng output, vào **Manage Jenkins → System → Git Host Key Verification Configuration** → chọn **Accept first connection** (cho learning), hoặc paste vào file `/var/jenkins_home/.ssh/known_hosts` trong container:

```bash
jenkins-admin@ci:~$ docker exec -i urbanx-jenkins bash -c \
  "mkdir -p /var/jenkins_home/.ssh && ssh-keyscan -t ed25519 <deploy-vps-ip> >> /var/jenkins_home/.ssh/known_hosts"
```

---

## 4.3. Secret file — `.env.staging`

Đây là file chứa toàn bộ biến môi trường runtime của staging (DB password, RabbitMQ password, JWT signing key…). Jenkins sẽ SCP lên Deploy VPS mỗi lần deploy.

Tạo file `.env.staging` ở máy local trước (sẽ refine ở bước 6):

```ini
# Image registry
DOCKER_HUB_USERNAME=<dockerhub-username>
IMAGE_TAG=develop-latest

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

# Identity Service
IDENTITY_ISSUER_URI=https://staging.urbanx.dev
IDENTITY_SIGNING_KEY=<openssl-rand-base64-64>

# ASP.NET
ASPNETCORE_ENVIRONMENT=Staging
```

Trong Jenkins:

| Field | Giá trị |
|---|---|
| Kind | Secret file |
| ID | `staging-env-file` |
| File | upload file `.env.staging` |

> Mỗi khi cần rotate secret: edit credential này upload file mới, build lại pipeline.

---

## 4.4. GitHub Personal Access Token (cho webhook + checkout private repo)

Nếu repo private cần PAT. Vào GitHub → Settings → Developer settings → **Fine-grained tokens** → Generate.

- Repository access: `urbanx` repo.
- Permissions:
  - Contents: Read
  - Metadata: Read
  - Webhooks: Read & Write
- Expiration: 90 days (rotate định kỳ).

Trong Jenkins:

| Field | Giá trị |
|---|---|
| Kind | Username with password |
| ID | `github-pat` |
| Username | GitHub username |
| Password | `<paste PAT>` |

Hoặc nếu repo public, skip — Jenkins clone qua HTTPS không cần auth.

---

## 4.5. Test SSH credential trong pipeline

Tạo job test "Pipeline" với script:

```groovy
pipeline {
  agent any
  stages {
    stage('Test SSH') {
      steps {
        sshagent(credentials: ['staging-ssh-deploy']) {
          sh '''
            ssh -o StrictHostKeyChecking=accept-new deploy@<deploy-vps-ip> "hostname && docker version --format '{{.Server.Version}}'"
          '''
        }
      }
    }
    stage('Test Docker Hub login') {
      steps {
        withCredentials([usernamePassword(
          credentialsId: 'docker-hub-creds',
          usernameVariable: 'DH_USER',
          passwordVariable: 'DH_PASS'
        )]) {
          sh 'echo "$DH_PASS" | docker login -u "$DH_USER" --password-stdin'
        }
      }
    }
    stage('Test env file') {
      steps {
        withCredentials([file(credentialsId: 'staging-env-file', variable: 'ENV_FILE')]) {
          sh 'head -3 "$ENV_FILE"'
        }
      }
    }
  }
}
```

Run job — tất cả stage phải xanh.

---

## 4.6. Tóm tắt credentials

| ID | Kind | Dùng ở stage |
|---|---|---|
| `docker-hub-creds` | Username/password | Login + push image |
| `staging-ssh-deploy` | SSH username + private key | SCP/SSH lên Deploy VPS |
| `staging-env-file` | Secret file | Copy `.env.staging` vào `/opt/urbanx/env/` |
| `github-pat` | Username/password | Clone private repo (nếu cần) |

Xong sang [05-dockerfiles.md](05-dockerfiles.md).
