# Bước 9 — GitHub Webhook → Jenkins

Mục tiêu: push code lên `develop` → GitHub gửi webhook → Jenkins auto-trigger build, không phải vào UI click "Build Now".

---

## 9.1. Bật endpoint webhook trên Jenkins

Jenkins LTS đã có sẵn endpoint `https://<jenkins-url>/github-webhook/` — chỉ cần đảm bảo:

1. **Manage Jenkins → System** → trong section **GitHub** → "Add GitHub Server":
   - Name: `github`
   - API URL: `https://api.github.com`
   - Credentials: `github-pat` (đã tạo ở bước 4) — kiểu **Secret text** (PAT), không phải Username/password.
   - Tick **Manage hooks** = false (tự setup webhook thủ công, an toàn hơn).
   - Click **Test connection** → "Credentials verified for user xxx, rate limit: 5000".

2. Plugin **GitHub Integration** đã cài (bước 3.6).

Endpoint webhook: `https://ci.urbanx.dev/github-webhook/` (chú ý dấu `/` cuối).

---

## 9.2. Tạo Secret để xác thực webhook (HMAC)

Trên Jenkins:

1. **Manage Jenkins → System → GitHub** → "Shared secrets" → Add Secret.
2. Generate ngẫu nhiên 1 chuỗi:
   ```bash
   $ openssl rand -hex 32
   ```
3. Lưu vào credentials kind **Secret text** → ID `github-webhook-secret`.
4. Chọn credential này trong field "Shared secrets".

Trên GitHub sẽ paste cùng secret này → mỗi request webhook GitHub ký HMAC-SHA256, Jenkins verify.

---

## 9.3. Tạo webhook trên GitHub repository

`https://github.com/<user>/urbanx` → **Settings → Webhooks → Add webhook**.

| Field | Giá trị |
|---|---|
| Payload URL | `https://ci.urbanx.dev/github-webhook/` |
| Content type | `application/json` |
| Secret | (paste chuỗi vừa generate ở 9.2) |
| SSL verification | Enable SSL verification |
| Which events | **Just the push event** (đủ cho develop branch) |
| Active | ✅ |

Click **Add webhook**.

GitHub gửi 1 ping request → kiểm tra tab "Recent Deliveries" → response code phải là **200**.

> Nếu trả 403/404: kiểm tra Jenkins URL có đúng `/github-webhook/` không (dấu `/` cuối quan trọng), Jenkins có nhìn thấy public không.
> Nếu 500: check log Jenkins `docker logs urbanx-jenkins`.

---

## 9.4. Cấu hình Multibranch job nhận webhook

Trong job `urbanx`:

1. **Configure** → tới section **Scan Repository Triggers**.
2. Tick **Periodically if not otherwise run**: 1 day (fallback).
3. Scroll xuống **Build Configuration** xác nhận script path: `Jenkinsfile`.
4. Save.

Quan trọng nhất: **Branch Sources → GitHub source → Behaviors**:
- Discover branches: All branches (hoặc filter chỉ `develop` + `main` để tiết kiệm scan).

Khi webhook đến, Jenkins re-scan multibranch → thấy branch `develop` có commit mới → trigger job con.

---

## 9.5. Filter — chỉ deploy khi push lên `develop`

Pipeline Jenkinsfile đã có guard `when { branch 'develop' }` cho các stage deploy. Branch khác sẽ chỉ build + test, không deploy.

Nếu muốn skip toàn bộ pipeline cho branch nhất định, dùng `when not { branch ... }` ở stage Checkout, hoặc cấu hình "Filter by name (with wildcards)" trong Branch Sources.

---

## 9.6. Test webhook end-to-end

```bash
# Trên máy local
$ git checkout develop
$ echo "// trigger ci" >> README.md
$ git add README.md
$ git commit -m "ci: test webhook trigger"
$ git push origin develop
```

Vào Jenkins → `urbanx/develop` → build mới phải xuất hiện trong vòng 5–10 giây.

Vào GitHub webhook detail → Recent Deliveries → tab Response phải là 200.

---

## 9.7. Bonus — Pull Request triggers

Để build PR vào `develop` (validation only, không deploy):

1. Trong GitHub Webhook đã tạo, thêm event **Pull requests**.
2. Multibranch job: **Branch Sources → Behaviors → Add**:
   - "Discover pull requests from origin" — Strategy: "Merging the pull request with the current target branch revision".

Jenkinsfile sẽ chạy với `env.BRANCH_NAME = 'PR-42'`, stage `when { branch 'develop' }` skip → chỉ build + test, không deploy. Tốt cho code review gate.

---

## 9.8. Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| Webhook 403 | Sai HMAC secret | Re-paste secret ở cả 2 phía |
| Webhook 404 | Sai URL hoặc thiếu `/` cuối | `https://ci.urbanx.dev/github-webhook/` |
| Webhook 200 nhưng Jenkins không build | Job chưa scan branch | Manual scan: job → "Scan Multibranch Pipeline Now" |
| Build chạy nhưng `BRANCH_NAME` lệch | Cache cũ | Disable job → Save → enable lại |
| GitHub gửi nhưng Jenkins không nhận | Firewall chặn IP GitHub | Mở 443 vào Jenkins từ [IP range GitHub webhook](https://api.github.com/meta) |

Xong sang [10-rollback-monitoring.md](10-rollback-monitoring.md).
