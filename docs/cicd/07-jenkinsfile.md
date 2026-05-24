# Bước 7 — Jenkinsfile

Mục tiêu: đặt `Jenkinsfile` ở root repo (`/Jenkinsfile`), Jenkins tự checkout & chạy theo declarative pipeline mỗi khi push lên `develop`.

---

## 7.1. Tạo Multibranch Pipeline trong Jenkins

UI Jenkins:

1. **New Item** → tên `urbanx` → kiểu **Multibranch Pipeline** → OK.
2. **Branch Sources** → Add source → **GitHub**:
   - Credentials: `github-pat` (đã tạo ở bước 4).
   - Repository HTTPS URL: `https://github.com/<user>/urbanx.git`.
   - Behaviors: giữ default (discover branches, PRs from origin).
3. **Build Configuration** → Mode: **by Jenkinsfile** → Script Path: `Jenkinsfile`.
4. **Scan Multibranch Pipeline Triggers**: tick "Periodically if not otherwise run" → 1 hour (fallback nếu webhook fail).
5. **Save**.

Jenkins scan repo, thấy `Jenkinsfile` ở branch `develop` → tự tạo job con `urbanx/develop`.

---

## 7.2. File `Jenkinsfile` (đặt ở root repo)

```groovy
pipeline {
    agent any

    options {
        timestamps()
        ansiColor('xterm')
        timeout(time: 30, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
        disableConcurrentBuilds()
    }

    environment {
        // Image tagging
        DOCKER_HUB_USERNAME = 'your-dockerhub-username'   // thay bằng username thật
        SHORT_SHA           = "${env.GIT_COMMIT?.take(7) ?: 'nogit'}"
        IMAGE_TAG           = "${env.BRANCH_NAME}-${env.BUILD_NUMBER}-${SHORT_SHA}"
        BRANCH_LATEST_TAG   = "${env.BRANCH_NAME}-latest"

        // Deploy target
        DEPLOY_HOST = 'deploy@<deploy-vps-ip-or-domain>'
        DEPLOY_PATH = '/opt/urbanx'

        // .NET
        DOTNET_NOLOGO              = 'true'
        DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
        NUGET_PACKAGES             = "${WORKSPACE}/.nuget/packages"
    }

    stages {

        // ───────── Build & Test ─────────

        stage('Checkout') {
            steps {
                checkout scm
                sh 'git rev-parse HEAD'
            }
        }

        stage('Restore') {
            steps {
                sh 'dotnet restore UrbanX.sln'
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet build UrbanX.sln -c Release --no-restore'
            }
        }

        stage('Test') {
            steps {
                sh '''
                    dotnet test UrbanX.sln \
                        -c Release \
                        --no-build \
                        --logger "trx;LogFileName=test-results.trx" \
                        --results-directory ./TestResults \
                        --collect:"XPlat Code Coverage"
                '''
            }
            post {
                always {
                    junit allowEmptyResults: true, testResults: '**/TestResults/*.trx'
                }
            }
        }

        // ───────── EF Migrations bundle ─────────

        stage('Build EF Bundles') {
            steps {
                sh '''
                    mkdir -p ./bundles
                    dotnet ef migrations bundle \
                        --self-contained -r linux-x64 \
                        --project src/Services/Catalog/UrbanX.Catalog.Persistence \
                        --startup-project src/Services/Catalog/UrbanX.Catalog.API \
                        --context CatalogDbContext \
                        --output ./bundles/catalog-efbundle --force

                    dotnet ef migrations bundle \
                        --self-contained -r linux-x64 \
                        --project src/Services/Identity/UrbanX.Identity.Persistence \
                        --startup-project src/Services/Identity/UrbanX.Identity.API \
                        --context IdentityDbContext \
                        --output ./bundles/identity-efbundle --force

                    dotnet ef migrations bundle \
                        --self-contained -r linux-x64 \
                        --project src/Services/Inventory/UrbanX.Inventory.Persistence \
                        --startup-project src/Services/Inventory/UrbanX.Inventory.API \
                        --context InventoryDbContext \
                        --output ./bundles/inventory-efbundle --force
                '''
            }
            post {
                success {
                    archiveArtifacts artifacts: 'bundles/*', fingerprint: true
                }
            }
        }

        // ───────── Docker build & push ─────────

        stage('Docker Login') {
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

        stage('Build Images') {
            parallel {
                stage('gateway') {
                    steps { buildImage('gateway',   'src/Gateway/Dockerfile') }
                }
                stage('catalog') {
                    steps { buildImage('catalog',   'src/Services/Catalog/Dockerfile') }
                }
                stage('identity') {
                    steps { buildImage('identity',  'src/Services/Identity/Dockerfile') }
                }
                stage('inventory') {
                    steps { buildImage('inventory', 'src/Services/Inventory/Dockerfile') }
                }
            }
        }

        stage('Push Images') {
            steps {
                sh '''
                    for svc in gateway catalog identity inventory; do
                        docker push ${DOCKER_HUB_USERNAME}/urbanx-${svc}:${IMAGE_TAG}
                        docker push ${DOCKER_HUB_USERNAME}/urbanx-${svc}:${BRANCH_LATEST_TAG}
                    done
                '''
            }
        }

        // ───────── Deploy ─────────

        stage('Sync compose + env to VPS') {
            when { branch 'develop' }
            steps {
                withCredentials([file(credentialsId: 'staging-env-file', variable: 'ENV_FILE')]) {
                    sshagent(credentials: ['staging-ssh-deploy']) {
                        sh '''
                            ssh -o StrictHostKeyChecking=accept-new ${DEPLOY_HOST} "mkdir -p ${DEPLOY_PATH}/compose ${DEPLOY_PATH}/env ${DEPLOY_PATH}/bundles"

                            scp -o StrictHostKeyChecking=accept-new docker-compose.staging.yml ${DEPLOY_HOST}:${DEPLOY_PATH}/compose/docker-compose.staging.yml
                            scp -o StrictHostKeyChecking=accept-new docker/init-multi-db.sh   ${DEPLOY_HOST}:${DEPLOY_PATH}/compose/init-multi-db.sh
                            scp -o StrictHostKeyChecking=accept-new "$ENV_FILE"               ${DEPLOY_HOST}:${DEPLOY_PATH}/env/.env.staging
                            scp -o StrictHostKeyChecking=accept-new bundles/*                 ${DEPLOY_HOST}:${DEPLOY_PATH}/bundles/

                            ssh ${DEPLOY_HOST} "chmod 600 ${DEPLOY_PATH}/env/.env.staging && chmod +x ${DEPLOY_PATH}/compose/init-multi-db.sh ${DEPLOY_PATH}/bundles/*"
                        '''
                    }
                }
            }
        }

        stage('Run DB Migrations') {
            when { branch 'develop' }
            steps {
                sshagent(credentials: ['staging-ssh-deploy']) {
                    sh '''
                        ssh ${DEPLOY_HOST} bash -se <<'EOF'
                          set -euo pipefail
                          cd /opt/urbanx
                          set -a; source env/.env.staging; set +a

                          docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml up -d postgres
                          # đợi postgres healthy
                          for i in $(seq 1 30); do
                            if docker compose -f compose/docker-compose.staging.yml exec -T postgres pg_isready -U "$POSTGRES_USER" >/dev/null 2>&1; then
                              echo "postgres ready"; break
                            fi
                            echo "waiting postgres... $i"; sleep 2
                          done

                          run_bundle() {
                            local svc=$1; local db=$2
                            local conn="Host=127.0.0.1;Port=5433;Database=${db};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
                            ./bundles/${svc}-efbundle --connection "$conn"
                          }

                          # publish 5433 tạm cho efbundle gọi từ host
                          docker compose -f compose/docker-compose.staging.yml stop postgres
                          docker run -d --rm --name urbanx-pg-migrate \
                            -e POSTGRES_USER=$POSTGRES_USER \
                            -e POSTGRES_PASSWORD=$POSTGRES_PASSWORD \
                            -e POSTGRES_MULTIPLE_DATABASES=$POSTGRES_DB_CATALOG,$POSTGRES_DB_IDENTITY,$POSTGRES_DB_INVENTORY \
                            -v urbanx-staging_postgres_data:/var/lib/postgresql/data \
                            -v /opt/urbanx/compose/init-multi-db.sh:/docker-entrypoint-initdb.d/init-multi-db.sh:ro \
                            -p 127.0.0.1:5433:5432 \
                            postgres:16-alpine

                          for i in $(seq 1 30); do
                            if docker exec urbanx-pg-migrate pg_isready -U "$POSTGRES_USER" >/dev/null 2>&1; then
                              break
                            fi
                            sleep 2
                          done

                          run_bundle catalog   "$POSTGRES_DB_CATALOG"
                          run_bundle identity  "$POSTGRES_DB_IDENTITY"
                          run_bundle inventory "$POSTGRES_DB_INVENTORY"

                          docker stop urbanx-pg-migrate
EOF
                    '''
                }
            }
        }

        stage('Deploy') {
            when { branch 'develop' }
            steps {
                sshagent(credentials: ['staging-ssh-deploy']) {
                    sh '''
                        ssh ${DEPLOY_HOST} bash -s <<EOF
set -euo pipefail
cd /opt/urbanx
# Override IMAGE_TAG mỗi lần deploy
export IMAGE_TAG=${IMAGE_TAG}
sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=${IMAGE_TAG}/' env/.env.staging

docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml pull
docker compose --env-file env/.env.staging -f compose/docker-compose.staging.yml up -d --remove-orphans

# Dọn image cũ
docker image prune -af --filter "until=72h"
EOF
                    '''
                }
            }
        }

        stage('Smoke Test') {
            when { branch 'develop' }
            steps {
                sshagent(credentials: ['staging-ssh-deploy']) {
                    sh '''
                        ssh ${DEPLOY_HOST} bash -s <<'EOF'
set -euo pipefail
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:5000/alive >/dev/null; then
    echo "gateway alive"; exit 0
  fi
  echo "waiting gateway... $i"
  sleep 5
done
echo "gateway not healthy"
docker compose -f /opt/urbanx/compose/docker-compose.staging.yml logs --tail=100 gateway
exit 1
EOF
                    '''
                }
            }
        }
    }

    post {
        always {
            sh 'docker logout || true'
            cleanWs(deleteDirs: true, notFailBuild: true,
                    patterns: [[pattern: '.nuget/packages', type: 'EXCLUDE']])
        }
        success {
            echo "✅ Deploy ${IMAGE_TAG} -> staging OK"
        }
        failure {
            echo "❌ Build/deploy failed at branch ${env.BRANCH_NAME}"
            // optional: Slack/email notification
        }
    }
}

// ─────────── Helper ───────────
def buildImage(String name, String dockerfile) {
    sh """
        docker build \
            --pull \
            --build-arg BUILD_CONFIGURATION=Release \
            -f ${dockerfile} \
            -t ${DOCKER_HUB_USERNAME}/urbanx-${name}:${IMAGE_TAG} \
            -t ${DOCKER_HUB_USERNAME}/urbanx-${name}:${BRANCH_LATEST_TAG} \
            .
    """
}
```

---

## 7.3. Giải thích các quyết định

| Quyết định | Lý do |
|---|---|
| `agent any` | Jenkins single-node — không phân biệt master/agent |
| `disableConcurrentBuilds()` | Tránh 2 build cùng deploy đè image lên nhau |
| `disableConcurrentBuilds()` + `timeout 30m` | Học → fail fast, không build dài lê thê |
| `buildDiscarder(numToKeepStr: 20)` | Giữ 20 build gần nhất → đủ rollback |
| Tag image 2 lớp (versioned + latest) | Rollback bằng tag versioned; pull "nhanh" bằng latest |
| `parallel` build images | 4 images build song song giảm ~ 60% thời gian |
| `sshagent` + `scp` | Đơn giản, không cần Ansible cho 1 host |
| `dotnet ef migrations bundle --self-contained -r linux-x64` | Binary chạy được trên Deploy VPS không cần cài .NET runtime |
| Tách stage Migrations trước Deploy | Schema sẵn sàng khi app start → tránh app crash loop |

---

## 7.4. Test pipeline lần đầu

Push commit lên `develop`:

```bash
$ git checkout -b develop
$ git push -u origin develop
```

Vào Jenkins → `urbanx/develop` → "Build Now" (manual lần đầu để verify, lần sau webhook tự trigger).

Theo dõi console output stage-by-stage. Stage hay fail nhất:
- **Restore**: thiếu csproj trong Dockerfile → sửa Dockerfile.
- **Test**: 1 unit test fail → fix code.
- **Push Images**: sai credentials → kiểm tra `docker-hub-creds`.
- **Run DB Migrations**: connection string sai port → check section 7.5.
- **Smoke Test**: gateway route đến service sai → check env trong compose.

---

## 7.5. Lưu ý migration stage

Stage `Run DB Migrations` ở trên giả định efbundle chạy từ Deploy VPS host kết nối đến Postgres container qua `127.0.0.1:5433`. Có cách đơn giản hơn — đặt efbundle vào image riêng và chạy như sidecar trong Docker network. Xem chi tiết ở [08-database-migrations.md](08-database-migrations.md).

---

## 7.6. Cải tiến sau này

- Thêm stage `SonarQube` / `dotnet format --verify-no-changes` cho code quality.
- Thêm stage `Trivy` scan image vuln trước khi push.
- Tách `Jenkinsfile` thành `Jenkinsfile.ci` (build+test) và `Jenkinsfile.cd` (deploy) khi triển khai thêm prod.
- Thêm stage manual `input` trước Deploy khi cần human approval.

Xong sang [08-database-migrations.md](08-database-migrations.md).
